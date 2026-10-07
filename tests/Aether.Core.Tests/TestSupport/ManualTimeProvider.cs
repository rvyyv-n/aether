using System;
using System.Threading;

namespace Aether.Core.Tests.TestSupport;

/// <summary>
/// Controllable <see cref="TimeProvider"/> for unit tests.
/// Overrides <see cref="GetTimestamp"/> and <see cref="TimestampFrequency"/> and allows manual advancement.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public ManualTimeProvider(long initialTimestamp = 0)
    {
        if (initialTimestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTimestamp), "Timestamp cannot be negative.");
        }

        _timestamp = initialTimestamp;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Cannot advance time backwards.");
        }

        Interlocked.Add(ref _timestamp, delta.Ticks);
    }
}
