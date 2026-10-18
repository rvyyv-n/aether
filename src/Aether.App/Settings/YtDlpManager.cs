using System.Net;
using System.Net.Http;
using Aether.Core.Playback;
using Aether.Core.Playback.Streams;

namespace Aether_App.Settings;

internal enum YtDlpInstallOutcome
{
    Installed,
    AlreadyCurrent,
    Offline,
    HashMismatch,
    Failed,
}

/// <summary>
/// Downloads yt-dlp from its official GitHub release into Aether's own folder under the user's profile, checked against
/// the release's published SHA-256, and keeps it fresh. Nothing is downloaded until something asks for it.
/// </summary>
internal static class YtDlpManager
{
    private const string Releases = "https://github.com/yt-dlp/yt-dlp/releases";
    private const string FileName = "yt-dlp.exe";

    private static readonly HttpClient Http = CreateClient(followRedirects: true);
    private static readonly HttpClient NoRedirectHttp = CreateClient(followRedirects: false);
    private static readonly SemaphoreSlim Busy = new(1, 1);
    private static int s_schedulerStarted;

    /// <summary>The folder that holds the downloaded copy; the uninstaller removes it.</summary>
    public static string ToolsFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "tools");

    public static string DownloadedPath => Path.Combine(ToolsFolder, FileName);

    private static string LastCheckPath => Path.Combine(ToolsFolder, "last-check.txt");

    /// <summary>
    /// Fetches the latest release. With <paramref name="onlyIfNewer"/> it does nothing when the copy in use is already
    /// that version. Progress runs from 0 to 1. A download that fails its hash check is deleted and never used.
    /// </summary>
    public static async Task<(YtDlpInstallOutcome Outcome, string? Version)> InstallLatestAsync(
        bool onlyIfNewer, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await Busy.WaitAsync(cancellationToken).ConfigureAwait(false);
        string partial = DownloadedPath + ".download";
        try
        {
            string? tag = await LatestTagAsync(cancellationToken).ConfigureAwait(false);
            if (tag is null)
            {
                return (YtDlpInstallOutcome.Failed, null);
            }
            string? current = await ExternalTools.RefreshYtDlpAsync().ConfigureAwait(false) is { } inUse
                ? ExternalTools.CachedVersion(inUse)
                : null;
            if (onlyIfNewer && current is not null && YtDlpVersions.Compare(current, tag) >= 0)
            {
                return (YtDlpInstallOutcome.AlreadyCurrent, current);
            }

            Directory.CreateDirectory(ToolsFolder);
            string sums = await Http.GetStringAsync($"{Releases}/download/{tag}/SHA2-256SUMS", cancellationToken).ConfigureAwait(false);
            string? expected = YtDlpDownloadCheck.ExpectedHash(sums, FileName);

            await DownloadAsync($"{Releases}/download/{tag}/{FileName}", partial, progress, cancellationToken).ConfigureAwait(false);

            string actual;
            await using (var downloaded = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                actual = await YtDlpDownloadCheck.HashAsync(downloaded, cancellationToken).ConfigureAwait(false);
            }
            if (!YtDlpDownloadCheck.Matches(expected, actual))
            {
                return (YtDlpInstallOutcome.HashMismatch, null);
            }

            File.Move(partial, DownloadedPath, overwrite: true);
            string? installed = await ExternalTools.YtDlpVersionAsync(DownloadedPath).ConfigureAwait(false);
            await ExternalTools.RefreshYtDlpAsync().ConfigureAwait(false);
            await RecordCheckAsync().ConfigureAwait(false);
            return (YtDlpInstallOutcome.Installed, installed ?? tag);
        }
        catch (HttpRequestException)
        {
            return (YtDlpInstallOutcome.Offline, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or TaskCanceledException)
        {
            // A timeout surfaces as TaskCanceledException without the caller having cancelled.
            cancellationToken.ThrowIfCancellationRequested();
            return (YtDlpInstallOutcome.Failed, null);
        }
        finally
        {
            try
            {
                File.Delete(partial);
            }
            catch (IOException)
            {
                // Left until the next attempt overwrites it.
            }
            Busy.Release();
        }
    }

    /// <summary>
    /// Starts the daily check, once per launch. It only updates a yt-dlp that is already there, toasts when a newer one
    /// arrives, and stays quiet on failure, trying again in an hour.
    /// </summary>
    public static void StartScheduledUpdates()
    {
        if (Interlocked.Exchange(ref s_schedulerStarted, 1) == 0)
        {
            _ = Task.Run(RunSchedulerAsync);
        }
    }

    /// <summary>The plain-English reason a download did not finish.</summary>
    public static string FailureKey(YtDlpInstallOutcome outcome) => outcome switch
    {
        YtDlpInstallOutcome.Offline => "YtDlp_InstallFailedOffline",
        YtDlpInstallOutcome.HashMismatch => "YtDlp_InstallFailedHash",
        _ => "YtDlp_InstallFailedOther",
    };

    private static async Task RunSchedulerAsync()
    {
        await App.SettingsLoaded.ConfigureAwait(false);
        await ExternalTools.RefreshYtDlpAsync().ConfigureAwait(false);
        while (true)
        {
            bool enabled = App.Settings.Current.YtDlpAutoUpdate;
            DateTimeOffset? last = await ReadLastCheckAsync().ConfigureAwait(false);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            TimeSpan wait = TimeSpan.FromHours(1);
            if (YtDlpUpdateSchedule.IsDue(enabled, last, now))
            {
                if (ExternalTools.FindYtDlp() is not null)
                {
                    (YtDlpInstallOutcome outcome, string? version) = await InstallLatestAsync(onlyIfNewer: true).ConfigureAwait(false);
                    if (outcome == YtDlpInstallOutcome.AlreadyCurrent)
                    {
                        await RecordCheckAsync().ConfigureAwait(false);
                    }
                    else if (outcome == YtDlpInstallOutcome.Installed && version is not null)
                    {
                        App.RunOnUi(() => AppToasts.Show("ytdlp-update", AppStrings.Format("Toast_YtDlpUpdated", version), ToastTone.Success));
                    }
                }
            }
            else if (enabled)
            {
                wait = YtDlpUpdateSchedule.DueIn(last, now) + TimeSpan.FromSeconds(1);
            }
            await Task.Delay(wait).ConfigureAwait(false);
        }
    }

    private static async Task DownloadAsync(string url, string destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            done += read;
            if (total is > 0)
            {
                progress?.Report(Math.Min(1.0, (double)done / total.Value));
            }
        }
    }

    // GitHub answers /latest with a redirect to /tag/<version>; reading the tag pins the hash file and the exe to one release.
    private static async Task<string?> LatestTagAsync(CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await NoRedirectHttp.GetAsync($"{Releases}/latest", cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Found or HttpStatusCode.MovedPermanently or HttpStatusCode.SeeOther
            && response.Headers.Location is { } location)
        {
            string tag = location.OriginalString.TrimEnd('/').Split('/')[^1];
            return tag.Length > 0 && tag.All(c => char.IsAsciiLetterOrDigit(c) || c == '.') ? tag : null;
        }
        return null;
    }

    private static async Task RecordCheckAsync()
    {
        Directory.CreateDirectory(ToolsFolder);
        await File.WriteAllTextAsync(LastCheckPath, DateTimeOffset.UtcNow.ToString("O")).ConfigureAwait(false);
    }

    private static async Task<DateTimeOffset?> ReadLastCheckAsync()
    {
        try
        {
            return DateTimeOffset.TryParse(await File.ReadAllTextAsync(LastCheckPath).ConfigureAwait(false), out DateTimeOffset when) ? when : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient(bool followRedirects)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = followRedirects }) { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Aether");
        return client;
    }
}
