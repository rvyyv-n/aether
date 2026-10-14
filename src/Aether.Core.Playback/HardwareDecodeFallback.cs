namespace Aether.Core.Playback;

/// <summary>
/// Spots mpv giving up on a hardware decoder mid-playback. <c>hwdec-current</c> reads "no" for software decoding, and
/// mpv reports it that way for files that never had a hardware path (auto-safe on an unsupported codec), so only a
/// switch from a decoder that was working counts.
/// </summary>
public static class HardwareDecodeFallback
{
    /// <param name="previous"><c>hwdec-current</c> before the change; null when it was unavailable.</param>
    /// <param name="current"><c>hwdec-current</c> after the change.</param>
    public static bool Occurred(string? previous, string? current) =>
        IsHardware(previous) && string.Equals(current, "no", StringComparison.Ordinal);

    private static bool IsHardware(string? decoder) =>
        !string.IsNullOrEmpty(decoder) && !string.Equals(decoder, "no", StringComparison.Ordinal);
}
