using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Thumbnails;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace Aether_App.Playback;

/// <summary>
/// Persistent disk cache of 16:9 poster thumbnails for Home Recents cards (<c>%LOCALAPPDATA%\Aether\thumbnails</c>).
/// Checks disk first, then Windows Shell thumbnails, falling back to a one-shot software libmpv frame grab for
/// formats like MKV/WebM that Windows Explorer does not thumbnail. Never runs on the UI thread.
/// </summary>
internal static class RecentThumbnailCache
{
    private const uint ShellThumbnailSize = 320;
    private const long MaxRemoteBytes = 4 * 1024 * 1024;

    private static readonly HttpClient s_http = CreateHttpClient();
    private static readonly SemaphoreSlim s_gate = new(2, 2);
    private static readonly string s_cacheDir = RecentDisplay.DefaultThumbnailCacheDirectory();

    public static string? TryGetCachedPath(string location, bool isLink)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        string cachePath = RecentDisplay.ThumbnailCachePath(s_cacheDir, location);
        try
        {
            var info = new FileInfo(cachePath);
            if (!info.Exists || info.Length == 0)
            {
                return null;
            }
            if (!isLink && File.Exists(location) && File.GetLastWriteTimeUtc(location) > info.LastWriteTimeUtc)
            {
                return null;
            }
            return cachePath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task<string?> SaveFromUrlAsync(
        string location,
        string? thumbnailUrl,
        IEnumerable<string> activeLocations,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(location)
            || string.IsNullOrWhiteSpace(thumbnailUrl)
            || !Uri.TryCreate(thumbnailUrl, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        await s_gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using HttpResponseMessage response = await s_http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode
                || response.Content.Headers.ContentLength > MaxRemoteBytes)
            {
                return null;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0 || bytes.Length > MaxRemoteBytes)
            {
                return null;
            }

            string cachePath = WriteAtomically(location, bytes);
            RecentDisplay.PruneThumbnailCache(s_cacheDir, activeLocations);
            return cachePath;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            s_gate.Release();
        }
    }

    public static async Task<string?> GetOrCreateLocalAsync(
        string path,
        double durationSeconds,
        IEnumerable<string> activeLocations,
        CancellationToken ct = default)
    {
        if (TryGetCachedPath(path, isLink: false) is { } hit)
        {
            return hit;
        }

        await s_gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (TryGetCachedPath(path, isLink: false) is { } secondHit)
            {
                return secondHit;
            }

            byte[]? shellBytes = await TryReadShellThumbnailAsync(path).ConfigureAwait(false);
            if (shellBytes is { Length: > 0 })
            {
                string saved = WriteAtomically(path, shellBytes);
                RecentDisplay.PruneThumbnailCache(s_cacheDir, activeLocations);
                return saved;
            }

            byte[]? decodedJpeg = await TryDecodeSingleFrameJpegAsync(path, durationSeconds, ct).ConfigureAwait(false);
            if (decodedJpeg is { Length: > 0 })
            {
                string saved = WriteAtomically(path, decodedJpeg);
                RecentDisplay.PruneThumbnailCache(s_cacheDir, activeLocations);
                return saved;
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            s_gate.Release();
        }
    }

    private static async Task<byte[]?> TryReadShellThumbnailAsync(string path)
    {
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            using StorageItemThumbnail thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem,
                ShellThumbnailSize,
                ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image || thumbnail.Size == 0)
            {
                return null;
            }

            using Stream stream = thumbnail.AsStreamForRead();
            using var ms = new MemoryStream((int)Math.Min(thumbnail.Size, MaxRemoteBytes));
            await stream.CopyToAsync(ms).ConfigureAwait(false);
            return ms.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]?> TryDecodeSingleFrameJpegAsync(string path, double durationSeconds, CancellationToken ct)
    {
        var ready = new TaskCompletionSource<ThumbnailFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var decoder = new LocalThumbnailDecoder((_, _, frame) => ready.TrySetResult(frame), cacheCapacity: 1);

        double probeSeconds = durationSeconds > 10
            ? Math.Min(durationSeconds * 0.1, 30.0)
            : (durationSeconds > 1 ? durationSeconds * 0.25 : 0.5);
        decoder.Request(path, bucket: 0, targetSeconds: probeSeconds);

        ThumbnailFrame frame;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(5));
            frame = await ready.Task.WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }

        using var ms = new MemoryStream();
        using IRandomAccessStream ras = ms.AsRandomAccessStream();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, ras);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            (uint)frame.Width,
            (uint)frame.Height,
            96,
            96,
            frame.BgraPixels);
        await encoder.FlushAsync();
        return ms.ToArray();
    }

    private static string WriteAtomically(string location, byte[] bytes)
    {
        Directory.CreateDirectory(s_cacheDir);
        string target = RecentDisplay.ThumbnailCachePath(s_cacheDir, location);
        string temp = target + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch
        {
            try { File.Delete(temp); } catch { }
            throw;
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether");
        return client;
    }
}
