namespace Aether.Core.Playback;

/// <summary>Options for <see cref="SeekCoalescer"/>.</summary>
public sealed record SeekCoalescerOptions
{
    /// <summary>
    /// Safety timeout after which an in-flight seek is assumed abandoned or lost,
    /// permitting subsequent seeks to dispatch. Defaults to 150 ms.
    /// </summary>
    public TimeSpan InFlightTimeout { get; init; } = TimeSpan.FromMilliseconds(150);

    public void Validate()
    {
        if (InFlightTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(InFlightTimeout), "Timeout must be positive.");
        }
    }
}
