using System.Globalization;

namespace Aether.PvP.Review.Clipping;

public static class ReEncodeArgumentsBuilder
{
    // videoBitsPerSecond set means a size limit; null means constant quality.
    public static IReadOnlyList<string> Build(
        ClipRequest request, string tempPath, VideoEncoder encoder, long? videoBitsPerSecond)
    {
        string start = request.StartTimeSeconds.ToString("F3", CultureInfo.InvariantCulture);
        string duration = (request.EndTimeSeconds - request.StartTimeSeconds).ToString("F3", CultureInfo.InvariantCulture);
        string container = FfmpegArguments.MuxerFor(Path.GetExtension(Path.ChangeExtension(tempPath, null)));

        // -ss before -i makes ffmpeg seek to the keyframe before the start and then decode up to
        // the exact frame, discarding what comes before it. -t as an output option then ends the
        // clip on the exact frame instead of the next keyframe.
        var args = new List<string>
        {
            "-hide_banner", "-nostdin", "-nostats", "-loglevel", "error",
            "-ss", start, "-i", request.SourceFilePath,
            "-t", duration,
            "-map", "0:v:0", "-map", "0:a:0?",
            "-c:v", encoder.FfmpegName()
        };

        // Filters run on CPU frames before any encoder, so hardware ones need no special case; -pix_fmt converts afterwards.
        if (request.VideoFilterChain is { Length: > 0 } filters)
            args.AddRange(["-vf", filters]);

        if (videoBitsPerSecond is { } bitrate)
        {
            string rate = bitrate.ToString(CultureInfo.InvariantCulture);
            // A big buffer lets a short clip spend most of its budget in the first burst; 1 s of
            // buffer for short clips, growing to 2 s once the clip is long enough to average out.
            double bufferSeconds = Math.Clamp((request.EndTimeSeconds - request.StartTimeSeconds) / 5, 1, 2);
            string buffer = ((long)(bitrate * bufferSeconds)).ToString(CultureInfo.InvariantCulture);
            args.AddRange(["-b:v", rate, "-maxrate", rate, "-bufsize", buffer]);
            if (encoder == VideoEncoder.Amf) args.AddRange(["-rc", "vbr_peak"]);
        }
        else
        {
            args.AddRange(QualityArguments(encoder));
        }

        args.AddRange(["-pix_fmt", "yuv420p", "-c:a", "aac", "-b:a", "128k"]);
        if (container is "mp4" or "mov")
            args.AddRange(["-movflags", "+faststart"]);

        args.AddRange(["-progress", "pipe:1", "-f", container, "-y", tempPath]);
        return args;
    }

    private static string[] QualityArguments(VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvenc => ["-preset", "p5", "-rc", "vbr", "-cq", "19", "-b:v", "0"],
        VideoEncoder.Qsv => ["-preset", "medium", "-global_quality", "20"],
        VideoEncoder.Amf => ["-quality", "quality", "-rc", "cqp", "-qp_i", "20", "-qp_p", "22"],
        _ => ["-preset", "medium", "-crf", "18"]
    };
}
