using Aether.Core.Playback;

namespace Aether.Core.Tests.TestSupport;

public readonly record struct RecordedSeekCommand(double Seconds, bool Dragging, ulong ReplyUserdata);

public sealed class FakeSeekCommandSender : ISeekCommandSender
{
    private readonly object _lock = new();
    private readonly List<RecordedSeekCommand> _sent = new();
    private Exception? _exceptionToThrow;

    public Exception? ExceptionToThrow
    {
        get { lock (_lock) return _exceptionToThrow; }
        set { lock (_lock) _exceptionToThrow = value; }
    }

    public Action<double, bool, ulong>? OnSend { get; set; }

    public IReadOnlyList<RecordedSeekCommand> SentCommands
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToArray();
            }
        }
    }

    public int SendCount
    {
        get
        {
            lock (_lock)
            {
                return _sent.Count;
            }
        }
    }

    public RecordedSeekCommand? LastCommand
    {
        get
        {
            lock (_lock)
            {
                return _sent.Count > 0 ? _sent[^1] : null;
            }
        }
    }

    public void SendSeek(double seconds, bool dragging, ulong replyUserdata)
    {
        lock (_lock)
        {
            _sent.Add(new RecordedSeekCommand(seconds, dragging, replyUserdata));
            if (_exceptionToThrow is not null)
            {
                throw _exceptionToThrow;
            }
        }

        OnSend?.Invoke(seconds, dragging, replyUserdata);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _sent.Clear();
        }
    }
}
