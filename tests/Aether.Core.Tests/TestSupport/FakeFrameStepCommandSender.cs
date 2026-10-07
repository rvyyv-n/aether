using System;
using System.Collections.Generic;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;

namespace Aether.Core.Tests.TestSupport;

/// <summary>
/// Recorded frame step command.
/// </summary>
public readonly record struct RecordedStepCommand(FrameStepDirection Direction, ulong ReplyUserdata);

/// <summary>
/// Recording fake <see cref="IFrameStepCommandSender"/> for unit tests.
/// </summary>
public sealed class FakeFrameStepCommandSender : IFrameStepCommandSender
{
    private readonly object _lock = new();
    private readonly List<RecordedStepCommand> _sent = new();
    private Exception? _exceptionToThrow;

    public Exception? ExceptionToThrow
    {
        get { lock (_lock) return _exceptionToThrow; }
        set { lock (_lock) _exceptionToThrow = value; }
    }

    public Action<FrameStepDirection, ulong>? OnSend { get; set; }

    public IReadOnlyList<RecordedStepCommand> SentCommands
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

    public RecordedStepCommand? LastCommand
    {
        get
        {
            lock (_lock)
            {
                return _sent.Count > 0 ? _sent[^1] : null;
            }
        }
    }

    public void Send(FrameStepDirection direction, ulong replyUserdata)
    {
        lock (_lock)
        {
            _sent.Add(new RecordedStepCommand(direction, replyUserdata));
            if (_exceptionToThrow is not null)
            {
                throw _exceptionToThrow;
            }
        }

        OnSend?.Invoke(direction, replyUserdata);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _sent.Clear();
        }
    }
}
