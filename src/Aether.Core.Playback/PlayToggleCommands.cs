using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// The mpv commands behind the Play/Pause button and Space. At the end of a file (<c>keep-open</c>
/// holds it paused on the last frame) toggling pause alone would stay at the end, so playback
/// restarts from 0 instead.
/// </summary>
public static class PlayToggleCommands
{
    private static readonly MpvCommand[] Toggle = [new("cycle", "pause")];
    private static readonly MpvCommand[] Restart = [new("seek", "0", "absolute"), new("set", "pause", "no")];

    /// <summary>The commands to send, in order, for a Play/Pause press.</summary>
    public static IReadOnlyList<MpvCommand> For(bool isAtEnd) => isAtEnd ? Restart : Toggle;
}
