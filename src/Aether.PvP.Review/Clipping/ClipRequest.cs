namespace Aether.PvP.Review.Clipping;

public enum ExportMode
{
    // Stream copy: instant and lossless, but the cut starts on a keyframe.
    Fast,
    // Re-encode: starts and ends on the exact frame, and can fit a size limit.
    Precise
}

public record ClipRequest(
    string SourceFilePath,
    double StartTimeSeconds,
    double EndTimeSeconds,
    string DestinationDirectory,
    string ClipNamePrefix = "Aether_PvP_Clip",
    ExportMode Mode = ExportMode.Fast,
    long? TargetSizeBytes = null,
    VideoEncoder? PreferredEncoder = null
);
