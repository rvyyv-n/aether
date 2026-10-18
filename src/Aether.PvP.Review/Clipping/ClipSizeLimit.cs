namespace Aether.PvP.Review.Clipping;

// The clip drawer's size limit in megabytes (0 = none), turned into bytes and checked against the clip length.
public static class ClipSizeLimit
{
    // Discord counts a megabyte as a million bytes, so the limit does too.
    public const long BytesPerMegabyte = 1_000_000;
    public const int MaxMegabytes = 4000;

    public static long? ToBytes(int megabytes) =>
        megabytes > 0 ? megabytes * BytesPerMegabyte : null;

    // The smallest size the clip can reach when the limit is too small for its length; null when it fits,
    // when there is no limit or when the length is unknown.
    public static long? MinimumIfTooSmall(int megabytes, double durationSeconds)
    {
        if (ToBytes(megabytes) is not { } bytes || durationSeconds <= 0)
            return null;

        BitratePlan plan = BitrateCalculator.Plan(bytes, durationSeconds);
        return plan.IsFeasible ? null : plan.MinimumSizeBytes;
    }

    // Rounded up to 0.1 MB, so the size shown is never one that would still be refused.
    public static double RoundUpMegabytes(long bytes) =>
        Math.Ceiling(bytes / (double)BytesPerMegabyte * 10) / 10;
}
