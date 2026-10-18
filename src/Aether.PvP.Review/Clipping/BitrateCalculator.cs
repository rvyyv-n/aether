namespace Aether.PvP.Review.Clipping;

public sealed record BitratePlan(bool IsFeasible, long VideoBitsPerSecond, long MinimumSizeBytes);

public sealed class ClipTargetTooSmallException : InvalidOperationException
{
    public ClipTargetTooSmallException(long minimumSizeBytes)
        : base($"The size limit is too small for this clip length. The smallest it can reach is about {minimumSizeBytes / 1024.0 / 1024.0:F1} MB.")
    {
        MinimumSizeBytes = minimumSizeBytes;
    }

    public long MinimumSizeBytes { get; }
}

public static class BitrateCalculator
{
    public const int AudioBitsPerSecond = 128_000;

    // Below this the picture is unusable blocks, so a smaller target is refused rather than encoded.
    public const int MinimumVideoBitsPerSecond = 100_000;

    // Encoders overshoot the requested rate and the container adds its own overhead, so only
    // this share of the limit is budgeted.
    public const double SafetyMargin = 0.93;

    public static BitratePlan Plan(long targetSizeBytes, double durationSeconds)
    {
        if (durationSeconds <= 0)
            return new BitratePlan(false, 0, 0);

        long minimumBytes = (long)Math.Ceiling(
            (MinimumVideoBitsPerSecond + AudioBitsPerSecond) * durationSeconds / 8.0 / SafetyMargin);

        if (targetSizeBytes <= 0)
            return new BitratePlan(false, 0, minimumBytes);

        double totalBitsPerSecond = targetSizeBytes * 8.0 * SafetyMargin / durationSeconds;
        long video = (long)Math.Floor(totalBitsPerSecond - AudioBitsPerSecond);
        return video < MinimumVideoBitsPerSecond
            ? new BitratePlan(false, 0, minimumBytes)
            : new BitratePlan(true, video, minimumBytes);
    }
}
