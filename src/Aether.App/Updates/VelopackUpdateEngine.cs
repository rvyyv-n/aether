using Aether.Core.Playback.Updates;
using Velopack;
using Velopack.Sources;

namespace Aether_App.Updates;

/// <summary>
/// The Velopack side of an update: reads <c>releases.win.json</c> from the repository's GitHub releases, downloads the full or delta
/// package (checked against the hashes in that file) and hands the swap to Velopack's Update.exe, which keeps the old version until
/// the new one is in place.
/// </summary>
internal sealed class VelopackUpdateEngine : IUpdateEngine
{
    // Pre-releases are included so beta testers move from one beta to the next (ADR-0021); revisit once 1.0 is out.
    private readonly UpdateManager _manager = new(new GithubSource(UpdateFeed.RepositoryUrl, null, prerelease: true));
    private UpdateInfo? _found;

    public bool IsInstalled => _manager.IsInstalled;

    public string? CurrentVersion => _manager.CurrentVersion?.ToString();

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        _found = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public async Task DownloadAsync(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        UpdateInfo found = _found ?? throw new InvalidOperationException("No update has been found.");
        await _manager.DownloadUpdatesAsync(found, percent => progress?.Report(percent / 100.0), cancellationToken).ConfigureAwait(false);
    }

    public void ApplyOnExit() => _manager.WaitExitThenApplyUpdates(FoundRelease(), silent: true, restart: false);

    public void ApplyAndRestart() => _manager.ApplyUpdatesAndRestart(FoundRelease());

    private VelopackAsset FoundRelease() => _found?.TargetFullRelease ?? throw new InvalidOperationException("No update has been downloaded.");
}
