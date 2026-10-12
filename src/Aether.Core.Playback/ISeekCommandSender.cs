using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback;

/// <summary>
/// Issues one seek command without waiting for it. The result comes back later as a command reply
/// carrying <c>replyUserdata</c> (see <see cref="IMpvCommandReplyHandler"/>).
/// </summary>
public interface ISeekCommandSender
{
    /// <summary>Sends one seek. Must not block. Throws <see cref="MpvException"/> if the core rejects it outright.</summary>
    void SendSeek(double seconds, bool dragging, ulong replyUserdata);
}

/// <summary>Sends seek commands to an mpv core carrying a tracked reply ID.</summary>
public sealed class MpvSeekCommandSender(MpvContext context) : ISeekCommandSender
{
    private readonly MpvContext _context = context ?? throw new ArgumentNullException(nameof(context));

    /// <inheritdoc />
    public void SendSeek(double seconds, bool dragging, ulong replyUserdata) =>
        _context.CommandAsync(TransportState.Seek(seconds, dragging), replyUserdata);
}
