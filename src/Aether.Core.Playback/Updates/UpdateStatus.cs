namespace Aether.Core.Playback.Updates;

public enum UpdateState
{
    /// <summary>Nothing has been checked yet this session.</summary>
    Idle,
    Checking,
    UpToDate,
    Downloading,
    /// <summary>Downloaded and queued; it installs when Aether closes, or at once on Restart.</summary>
    Ready,
    /// <summary>A zip copy or a development build: there is no installer to update through.</summary>
    NotInstalled,
    Failed,
}

/// <summary>What the update service is doing, for the About page and toasts. <see cref="Progress"/> runs 0 to 1 while downloading.</summary>
public sealed record UpdateStatus(UpdateState State, string? Version = null, double Progress = 0)
{
    public static readonly UpdateStatus Idle = new(UpdateState.Idle);

    /// <summary>Whether Check now should do anything: not while one is running or when there is no installer to update.</summary>
    public bool CanCheck => State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.NotInstalled);
}
