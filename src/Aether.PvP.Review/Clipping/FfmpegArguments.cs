using System.Globalization;

namespace Aether.PvP.Review.Clipping;

public static class FfmpegArguments
{
    public static IReadOnlyList<string> Build(ClipRequest request, string tempPath)
    {
        string start = request.StartTimeSeconds.ToString("F3", CultureInfo.InvariantCulture);
        string duration = (request.EndTimeSeconds - request.StartTimeSeconds).ToString("F3", CultureInfo.InvariantCulture);

        // -ss before -i is a fast container seek, and with -c copy there is no decoder to
        // run up to the exact frame: the cut starts on the keyframe at or before the requested
        // start, so the clip can begin up to one GOP early. That is the price of zero re-encoding.
        return
        [
            "-hide_banner", "-nostdin", "-nostats", "-loglevel", "error",
            "-ss", start, "-i", request.SourceFilePath,
            "-t", duration, "-map", "0", "-c", "copy",
            "-avoid_negative_ts", "make_zero",
            "-f", MuxerFor(Path.GetExtension(request.SourceFilePath)),
            "-y", tempPath
        ];
    }

    // The temp file ends in .part, so the muxer has to be named explicitly.
    internal static string MuxerFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".mkv" => "matroska",
        ".webm" => "webm",
        ".mov" => "mov",
        _ => "mp4"
    };
}
