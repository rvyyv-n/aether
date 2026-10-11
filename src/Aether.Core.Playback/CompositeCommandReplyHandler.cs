namespace Aether.Core.Playback;

/// <summary>
/// Combines multiple <see cref="IMpvCommandReplyHandler"/> instances and forwards replies
/// until one handles it. Zero allocation on dispatch.
/// </summary>
public sealed class CompositeCommandReplyHandler : IMpvCommandReplyHandler
{
    private readonly IMpvCommandReplyHandler[] _handlers;

    public CompositeCommandReplyHandler(params IMpvCommandReplyHandler[] handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = (IMpvCommandReplyHandler[])handlers.Clone();
    }

    /// <inheritdoc />
    public bool OnCommandReply(ulong replyUserdata, int error)
    {
        for (int i = 0; i < _handlers.Length; i++)
        {
            if (_handlers[i].OnCommandReply(replyUserdata, error))
            {
                return true;
            }
        }
        return false;
    }
}
