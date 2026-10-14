using Aether.Core.Playback;
using Xunit;

namespace Aether.Core.Tests;

public class DiagnosticsRowsTests
{
    private static readonly string[] Labels = ["Decoder", "Video", "Frame rate", "Display", "Dropped", "Cache"];

    [Fact]
    public void NothingReported_ShowsDashesAndZeroDrops()
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial);

        Assert.Equal(DiagnosticsRows.Unavailable, rows.Decoder);
        Assert.Equal(DiagnosticsRows.Unavailable, rows.Video);
        Assert.Equal(DiagnosticsRows.Unavailable, rows.FrameRate);
        Assert.Equal(DiagnosticsRows.Unavailable, rows.Display);
        Assert.Equal("0 · 0", rows.Dropped);
        Assert.Equal(DiagnosticsRows.Unavailable, rows.Cache);
    }

    [Fact]
    public void PlayingFile_FormatsEveryRow()
    {
        PlayerState state = PlayerState.Initial with
        {
            Decoder = "d3d11va",
            VideoFormat = "h264",
            CodedWidth = 1920,
            CodedHeight = 1080,
            FramesPerSecond = 23.976,
            EstimatedFps = 23.97,
            DisplayFps = 60,
            DecoderDroppedFrames = 2,
            DroppedFrames = 5,
            CacheDuration = 8.25,
        };

        DiagnosticsRows rows = DiagnosticsRows.From(state);

        Assert.Equal("Hardware (d3d11va)", rows.Decoder);
        Assert.Equal("h264 · 1920×1080", rows.Video);
        Assert.Equal("23.976 fps · 24 playing", rows.FrameRate);
        Assert.Equal("60 Hz", rows.Display);
        Assert.Equal("2 · 5", rows.Dropped);
        Assert.Equal("8.3 s", rows.Cache);
    }

    [Fact]
    public void SoftwareDecoding_IsNamedPlainly()
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial with { Decoder = "no" });

        Assert.Equal("Software", rows.Decoder);
    }

    [Theory]
    [InlineData(30, 0, "30 fps")]
    [InlineData(0, 29.97, "30 fps")]
    [InlineData(0, 0, "—")]
    public void FrameRate_UsesWhicheverRateIsKnown(double container, double estimated, string expected)
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial with { FramesPerSecond = container, EstimatedFps = estimated });

        Assert.Equal(expected, rows.FrameRate);
    }

    [Fact]
    public void Video_WithoutSize_ShowsTheCodecAlone()
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial with { VideoFormat = "av1" });

        Assert.Equal("av1", rows.Video);
    }

    [Fact]
    public void Cache_ZeroIsAValueNotMissing()
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial with { CacheDuration = 0 });

        Assert.Equal("0.0 s", rows.Cache);
    }

    [Fact]
    public void ToText_ListsTitleThenLabelledRows()
    {
        DiagnosticsRows rows = DiagnosticsRows.From(PlayerState.Initial with { Decoder = "no" });

        string[] lines = rows.ToText("Diagnostics", Labels).Split(Environment.NewLine);

        Assert.Equal(["Diagnostics", "Decoder: Software", "Video: —", "Frame rate: —", "Display: —", "Dropped: 0 · 0", "Cache: —"], lines);
    }

    [Fact]
    public void ToText_WrongLabelCount_Throws()
    {
        Assert.Throws<ArgumentException>(() => DiagnosticsRows.From(PlayerState.Initial).ToText("Diagnostics", ["Decoder"]));
    }
}
