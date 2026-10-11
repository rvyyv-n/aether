namespace Aether.Core.Playback;

/// <summary>
/// Decides when a looping file restarts. <c>keep-open</c> holds the last frame, so the end shows up as
/// <c>eof-reached</c> turning true; restarting once per such edge keeps a slow flush from sending twice.
/// The restart itself is the replay path of <see cref="PlayToggleCommands"/>.
/// </summary>
public sealed class LoopRestart
{
    private bool _wasAtEnd;

    /// <summary>Call with the latest state on each flush; true once when the file has just ended while looping.</summary>
    public bool ShouldRestart(bool isAtEnd, bool isLooping)
    {
        bool reached = isAtEnd && !_wasAtEnd;
        _wasAtEnd = isAtEnd;
        return reached && isLooping;
    }
}
