namespace Aether.Core.Playback;

/// <summary>
/// Receives <c>MPV_EVENT_COMMAND_REPLY</c> results on the event thread. Must not block.
/// Reply ids are partitioned by owner so one dispatcher can fan replies out:
/// <see cref="FrameStepReplyTag"/> marks frame-step commands.
/// </summary>
public interface IMpvCommandReplyHandler
{
    /// <summary>Upper 16 bits of every reply id sent by the frame stepper.</summary>
    public const ulong FrameStepReplyTag = 0x5354_0000_0000_0000;

    /// <summary>Upper 16 bits of every reply id sent by the seek coalescer.</summary>
    public const ulong SeekReplyTag = 0x534B_0000_0000_0000;

    /// <summary>Handles the reply and returns true if <paramref name="replyUserdata"/> belongs to this handler.</summary>
    bool OnCommandReply(ulong replyUserdata, int error);
}
