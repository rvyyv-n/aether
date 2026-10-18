namespace Aether.Core.Playback.Updates;

/// <summary>
/// The part of an update that depends on the installer (Velopack): finding, fetching and staging a release. The engine keeps the
/// release it found between calls, so <see cref="DownloadAsync"/> and the apply calls act on the last <see cref="CheckAsync"/>.
/// </summary>
public interface IUpdateEngine
{
    /// <summary>False for a portable zip or a development build, which have no installer to update through.</summary>
    bool IsInstalled { get; }

    /// <summary>The running version, or null when it is not known.</summary>
    string? CurrentVersion { get; }

    /// <summary>The version of a newer release on the feed, or null when this one is the latest.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads and verifies the release found by the last check; progress runs 0 to 1.</summary>
    Task DownloadAsync(IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Queues the downloaded release to install after Aether exits, without closing it.</summary>
    void ApplyOnExit();

    /// <summary>Closes Aether, installs the downloaded release and starts the new version.</summary>
    void ApplyAndRestart();
}
