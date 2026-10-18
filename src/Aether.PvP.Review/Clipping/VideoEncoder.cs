namespace Aether.PvP.Review.Clipping;

// Declared in fallback order: the hardware encoders first, the software one last.
public enum VideoEncoder
{
    Nvenc,
    Qsv,
    Amf,
    X264
}

public static class VideoEncoderExtensions
{
    public static string FfmpegName(this VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvenc => "h264_nvenc",
        VideoEncoder.Qsv => "h264_qsv",
        VideoEncoder.Amf => "h264_amf",
        _ => "libx264"
    };
}
