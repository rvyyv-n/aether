using System.Diagnostics;
using Aether.Core.Interop;
using Aether.Core.Playback.Streams;

namespace Aether_App.Settings;

/// <summary>Finds the helper programs Aether plays with and reads their versions, for the streaming and about pages.</summary>
internal static class ExternalTools
{
    private static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(3);
    private static readonly Dictionary<string, string?> YtDlpVersionCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The yt-dlp that streaming would use: the newest of Aether's downloaded copy, the one shipped beside the app and the
    /// first on PATH. Versions come from the last <see cref="RefreshYtDlpAsync"/>; until then the order alone decides.
    /// Null when there is none.
    /// </summary>
    public static string? FindYtDlp()
    {
        var found = new List<(string Path, string? Version)>();
        foreach (string path in YtDlpCandidates())
        {
            found.Add((path, CachedVersion(path)));
        }
        return YtDlpVersions.Choose(found);
    }

    /// <summary>Reads the version of every yt-dlp copy so <see cref="FindYtDlp"/> can pick the newest; returns its choice.</summary>
    public static async Task<string?> RefreshYtDlpAsync()
    {
        var versions = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in YtDlpCandidates())
        {
            versions[path] = await YtDlpVersionAsync(path).ConfigureAwait(false);
        }
        lock (YtDlpVersionCache)
        {
            YtDlpVersionCache.Clear();
            foreach ((string path, string? version) in versions)
            {
                YtDlpVersionCache[path] = version;
            }
        }
        return FindYtDlp();
    }

    public static string? CachedVersion(string path)
    {
        lock (YtDlpVersionCache)
        {
            return YtDlpVersionCache.GetValueOrDefault(path);
        }
    }

    private static IEnumerable<string> YtDlpCandidates()
    {
        if (File.Exists(YtDlpManager.DownloadedPath))
        {
            yield return YtDlpManager.DownloadedPath;
        }
        string beside = Path.Combine(AppContext.BaseDirectory, "yt-dlp.exe");
        if (File.Exists(beside))
        {
            yield return beside;
        }
        foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate;
            try
            {
                candidate = Path.Combine(folder.Trim().Trim('"'), "yt-dlp.exe");
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry; the rest may still be good.
                continue;
            }
            if (File.Exists(candidate))
            {
                yield return candidate;
                yield break;
            }
        }
    }

    /// <summary>The output of <c>yt-dlp --version</c>, or null when it does not answer in a few seconds.</summary>
    public static async Task<string?> YtDlpVersionAsync(string path)
    {
        try
        {
            var start = new ProcessStartInfo(path, "--version")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using Process? process = Process.Start(start);
            if (process is null)
            {
                return null;
            }
            using var timeout = new CancellationTokenSource(VersionTimeout);
            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeout.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                string version = output.Trim();
                return version.Length == 0 ? null : version;
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    /// <summary>The version stamped on libmpv-2.dll, or null when the file is missing or carries none.</summary>
    public static string? MpvVersion()
    {
        try
        {
            string path = Path.Combine(NativeLibraries.SearchDirectory ?? AppContext.BaseDirectory, "libmpv-2.dll");
            if (!File.Exists(path))
            {
                return null;
            }
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(path);
            string? version = info.ProductVersion ?? info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
