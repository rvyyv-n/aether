using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// Issues one frame step without waiting for it. The result must come back later as a command reply
/// carrying <c>replyUserdata</c> (see <see cref="IMpvCommandReplyHandler"/>).
/// </summary>
public interface IFrameStepCommandSender
{
    /// <summary>Sends one step. Must not block. Throws <see cref="MpvException"/> if the core rejects it outright.</summary>
    void Send(FrameStepDirection direction, ulong replyUserdata);
}

/// <summary>Sends frame steps to an mpv core through the pre-encoded <see cref="MpvCommand"/> instances; allocates nothing.</summary>
public sealed class MpvFrameStepCommandSender(MpvContext context) : IFrameStepCommandSender
{
    private readonly MpvContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public void Send(FrameStepDirection direction, ulong replyUserdata) =>
        _context.CommandAsync(direction == FrameStepDirection.Forward ? MpvCommand.FrameStep : MpvCommand.FrameBackStep, replyUserdata);
}
