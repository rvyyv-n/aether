namespace Aether.Core.Playback;

/// <summary>Timing of a held frame-step key. See <see cref="FrameRepeatCurve"/> for how the values combine.</summary>
public sealed class FrameStepperOptions
{
    private TimeSpan _initialRepeatDelay = TimeSpan.FromMilliseconds(250);
    private TimeSpan _startInterval = TimeSpan.FromMilliseconds(100);
    private TimeSpan _minInterval = TimeSpan.FromMilliseconds(33);
    private TimeSpan _accelerationTime = TimeSpan.FromSeconds(2);
    private TimeSpan _inFlightTimeout = TimeSpan.FromSeconds(1);

    /// <summary>Delay between the press's first step and the first repeat.</summary>
    public TimeSpan InitialRepeatDelay { get => _initialRepeatDelay; init => _initialRepeatDelay = Positive(value); }

    /// <summary>Interval at the first repeat.</summary>
    public TimeSpan StartInterval { get => _startInterval; init => _startInterval = Positive(value); }

    /// <summary>Fastest interval, reached after <see cref="AccelerationTime"/> of holding.</summary>
    public TimeSpan MinInterval { get => _minInterval; init => _minInterval = Positive(value); }

    /// <summary>Hold time after the first repeat over which the interval falls linearly from start to min.</summary>
    public TimeSpan AccelerationTime { get => _accelerationTime; init => _accelerationTime = Positive(value); }

    /// <summary>A step whose reply has not arrived after this long is treated as lost, so it cannot wedge stepping.</summary>
    public TimeSpan InFlightTimeout { get => _inFlightTimeout; init => _inFlightTimeout = Positive(value); }

    /// <summary>Checks the cross-property rules (<see cref="MinInterval"/> &lt;= <see cref="StartInterval"/>).</summary>
    /// <exception cref="ArgumentException">The combination is invalid.</exception>
    public void Validate()
    {
        if (_minInterval > _startInterval)
        {
            throw new ArgumentException("MinInterval cannot exceed StartInterval.");
        }
    }

    private static TimeSpan Positive(TimeSpan value) =>
        value > TimeSpan.Zero ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Must be positive.");
}
