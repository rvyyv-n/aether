namespace Aether.PvP.Review.Clipping;

public readonly record struct ExportRegion(
    double StartTimeSeconds,
    double EndTimeSeconds
)
{
    public double DurationSeconds => EndTimeSeconds > StartTimeSeconds ? EndTimeSeconds - StartTimeSeconds : 0.0;
    public bool IsValid => EndTimeSeconds > StartTimeSeconds;
}
