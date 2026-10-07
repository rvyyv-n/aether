using System;
using System.Threading;
using Aether.Core.Playback;

namespace Aether.Core.Tests.TestSupport;

public sealed class FakeUiFlushScheduler : IUiFlushScheduler
{
    private int _flushCount;
    private volatile bool _shouldThrow;

    public int RequestFlushCount => Volatile.Read(ref _flushCount);

    public bool ShouldThrow
    {
        get => _shouldThrow;
        set => _shouldThrow = value;
    }

    public void RequestFlush()
    {
        Interlocked.Increment(ref _flushCount);
        if (_shouldThrow)
        {
            throw new InvalidOperationException("Simulated RequestFlush failure");
        }
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _flushCount, 0);
        _shouldThrow = false;
    }
}
