using System;

namespace Aether.PvP.Review.Analysis;

public readonly record struct PlaybackMetric(
    double TimestampSeconds,
    long Milliseconds,
    long GameTick,
    long SubTickPercentage,
    long FrameNumber
);

public static class TickCalculator
{
    private const double SecondsPerTick = 0.050; // 50ms per Minecraft tick (20 ticks/sec)

    public static PlaybackMetric Compute(double timePosSeconds, double sourceFps = 0, double secondsPerTick = SecondsPerTick)
    {
        if (timePosSeconds < 0) timePosSeconds = 0;

        long ms = (long)(timePosSeconds * 1000.0);
        double tickPos = timePosSeconds / secondsPerTick;
        long tick = (long)Math.Floor(tickPos);
        long subTickPct = Math.Clamp((long)Math.Round((tickPos - tick) * 100.0), 0, 99);
        long frame = sourceFps > 0 ? (long)Math.Round(timePosSeconds * sourceFps) : 0;

        return new PlaybackMetric(timePosSeconds, ms, tick, subTickPct, frame);
    }
}
