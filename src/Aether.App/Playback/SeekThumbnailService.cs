using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using Aether.Core.Playback.Streams;
using Aether.Core.Playback.Thumbnails;
using Microsoft.UI.Dispatching;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Aether_App.Playback;

/// <summary>
/// Coordinates on-demand seek-bar hover thumbnails for local files (lazy headless libmpv) and
/// streamed links (yt-dlp storyboard sprite sheets), dispatching results back onto the UI thread.
/// </summary>
public sealed class SeekThumbnailService : IDisposable
{
    private readonly record struct DecodedSheet(int Width, int Height, byte[] BgraPixels);

    private static readonly HttpClient s_http = CreateHttpClient();

    private readonly DispatcherQueue _dispatcher;
    private readonly Action<int, int, ThumbnailFrame> _onFrameReady;
    private readonly LocalThumbnailDecoder _localDecoder;
    private readonly ThumbnailLruCache<string, DecodedSheet> _sheetCache = new(capacity: 8);
    private readonly ThumbnailLruCache<(int Generation, int Bucket), ThumbnailFrame> _streamFrameCache = new(capacity: 96);
    private readonly Lock _sheetLock = new();
    private readonly Dictionary<string, Task<DecodedSheet?>> _inFlightSheets = new(StringComparer.Ordinal);

    private int _generation;
    private string? _localPath;
    private StreamStoryboard? _storyboard;
    private bool _disposed;

    public SeekThumbnailService(DispatcherQueue dispatcher, Action<int, int, ThumbnailFrame> onFrameReady)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onFrameReady);
        _dispatcher = dispatcher;
        _onFrameReady = onFrameReady;
        _localDecoder = new LocalThumbnailDecoder(OnLocalFrameDecoded);
    }

    public int CurrentGeneration => Volatile.Read(ref _generation);

    public bool HasActiveSource => _localPath is not null || _storyboard is not null;

    public void SetLocalSource(string? path)
    {
        Interlocked.Increment(ref _generation);
        _storyboard = null;
        _localPath = string.IsNullOrWhiteSpace(path) ? null : path;
        if (_localPath is null)
        {
            _localDecoder.Reset();
        }
    }

    public void SetStreamSource(IReadOnlyList<StreamStoryboard>? storyboards)
    {
        Interlocked.Increment(ref _generation);
        _localPath = null;
        _localDecoder.Reset();
        _streamFrameCache.Clear();
        _storyboard = StoryboardIndex.SelectBest(storyboards);
    }

    public void ClearSource()
    {
        Interlocked.Increment(ref _generation);
        _localPath = null;
        _storyboard = null;
        _localDecoder.Reset();
    }

    /// <summary>
    /// Returns a cached frame synchronously when available; otherwise triggers background decoding/fetching
    /// and delivers the result through the callback provided at construction.
    /// </summary>
    public ThumbnailFrame? RequestPreview(double seconds, int bucket, double duration)
    {
        if (_disposed || duration <= 0)
        {
            return null;
        }

        int gen = Volatile.Read(ref _generation);
        if (_localPath is { } localPath)
        {
            if (_localDecoder.TryGetCached(localPath, bucket, out ThumbnailFrame cachedLocal))
            {
                return cachedLocal;
            }

            (_, double target) = ThumbnailTiming.QuantizeBucket(seconds, duration);
            _localDecoder.Request(localPath, bucket, target);
            return null;
        }

        if (_storyboard is { } storyboard)
        {
            if (_streamFrameCache.TryGet((gen, bucket), out ThumbnailFrame cachedStream))
            {
                return cachedStream;
            }

            (_, double target) = ThumbnailTiming.QuantizeBucket(seconds, duration);
            if (StoryboardIndex.Locate(storyboard, target) is not { } tile)
            {
                return null;
            }

            if (_sheetCache.TryGet(tile.SheetUrl, out DecodedSheet sheet)
                && SliceTile(sheet, tile) is { } sliced)
            {
                _streamFrameCache.Set((gen, bucket), sliced);
                return sliced;
            }

            _ = LoadStreamTileAsync(gen, bucket, storyboard, tile);
        }

        return null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Interlocked.Increment(ref _generation);
        _localDecoder.Dispose();
        _sheetCache.Clear();
        _streamFrameCache.Clear();
    }

    private void OnLocalFrameDecoded(string path, int bucket, ThumbnailFrame frame)
    {
        int gen = Volatile.Read(ref _generation);
        _dispatcher.TryEnqueue(() =>
        {
            if (_disposed || gen != Volatile.Read(ref _generation))
            {
                return;
            }
            if (string.Equals(_localPath, path, StringComparison.OrdinalIgnoreCase))
            {
                _onFrameReady(gen, bucket, frame);
            }
        });
    }

    private async Task LoadStreamTileAsync(int generation, int bucket, StreamStoryboard storyboard, StoryboardTile tile)
    {
        DecodedSheet? sheet = await GetOrFetchSheetAsync(tile.SheetUrl, storyboard.HttpHeaders).ConfigureAwait(false);
        if (_disposed || generation != Volatile.Read(ref _generation) || sheet is not { } decoded)
        {
            return;
        }

        if (SliceTile(decoded, tile) is not { } frame)
        {
            return;
        }

        _streamFrameCache.Set((generation, bucket), frame);
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed && generation == Volatile.Read(ref _generation))
            {
                _onFrameReady(generation, bucket, frame);
            }
        });
    }

    private Task<DecodedSheet?> GetOrFetchSheetAsync(string url, IReadOnlyDictionary<string, string>? headers)
    {
        if (_sheetCache.TryGet(url, out DecodedSheet cached))
        {
            return Task.FromResult<DecodedSheet?>(cached);
        }

        lock (_sheetLock)
        {
            if (_inFlightSheets.TryGetValue(url, out Task<DecodedSheet?>? existing))
            {
                return existing;
            }

            Task<DecodedSheet?> task = FetchAndDecodeSheetAsync(url, headers);
            _inFlightSheets[url] = task;
            return task;
        }
    }

    private async Task<DecodedSheet?> FetchAndDecodeSheetAsync(string url, IReadOnlyDictionary<string, string>? headers)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (headers is not null)
            {
                foreach ((string key, string value) in headers)
                {
                    request.Headers.TryAddWithoutValidation(key, value);
                }
            }

            using HttpResponseMessage response = await s_http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            byte[] encoded = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (encoded.Length == 0 || _disposed)
            {
                return null;
            }

            using var ms = new MemoryStream(encoded, writable: false);
            using IRandomAccessStream stream = ms.AsRandomAccessStream();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            PixelDataProvider provider = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] pixels = provider.DetachPixelData();
            int width = (int)decoder.PixelWidth;
            int height = (int)decoder.PixelHeight;
            if (width <= 0 || height <= 0 || pixels.Length < width * height * 4)
            {
                return null;
            }

            var sheet = new DecodedSheet(width, height, pixels);
            _sheetCache.Set(url, sheet);
            return sheet;
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            lock (_sheetLock)
            {
                _inFlightSheets.Remove(url);
            }
        }
    }

    private static ThumbnailFrame? SliceTile(DecodedSheet sheet, StoryboardTile tile)
    {
        if (sheet.Width <= 0 || sheet.Height <= 0)
        {
            return null;
        }

        int cropX = Math.Clamp(tile.CropX, 0, sheet.Width - 1);
        int cropY = Math.Clamp(tile.CropY, 0, sheet.Height - 1);
        int width = Math.Min(tile.Width, sheet.Width - cropX);
        int height = Math.Min(tile.Height, sheet.Height - cropY);
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int srcStride = sheet.Width * 4;
        int dstStride = width * 4;
        var bgra = new byte[dstStride * height];
        for (int row = 0; row < height; row++)
        {
            System.Buffer.BlockCopy(sheet.BgraPixels, (cropY + row) * srcStride + cropX * 4, bgra, row * dstStride, dstStride);
        }

        return new ThumbnailFrame(width, height, bgra);
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether");
        return client;
    }
}
