using System;
using System.Threading;

namespace Aether.Core.Tests.TestSupport;

/// <summary>
/// Controllable <see cref="TimeProvider"/> for unit tests.
/// Overrides <see cref="GetTimestamp"/> and <see cref="TimestampFrequency"/> and allows manual advancement.
/// </summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private readonly long _initialTimestamp;
    private readonly DateTimeOffset _startUtc = DateTimeOffset.UtcNow;
    private long _timestamp;

    public ManualTimeProvider(long initialTimestamp = 0)
    {
        if (initialTimestamp < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialTimestamp), "Timestamp cannot be negative.");
        }

        _initialTimestamp = initialTimestamp;
        _timestamp = initialTimestamp;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

    public override DateTimeOffset GetUtcNow() =>
        _startUtc.AddTicks(Interlocked.Read(ref _timestamp) - _initialTimestamp);

    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "Cannot advance time backwards.");
        }

        Interlocked.Add(ref _timestamp, delta.Ticks);
    }
}
