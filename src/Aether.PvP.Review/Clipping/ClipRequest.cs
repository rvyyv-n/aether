namespace Aether.PvP.Review.Clipping;

public record ClipRequest(
    string SourceFilePath,
    double StartTimeSeconds,
    double EndTimeSeconds,
    string DestinationDirectory,
    string ClipNamePrefix = "Aether_PvP_Clip"
);
