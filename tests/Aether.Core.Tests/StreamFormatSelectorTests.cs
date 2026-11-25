using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public sealed class StreamFormatSelectorTests
{
    private static StreamFormat Video(
        string id,
        int? height,
        double? fps = 30,
        string? vcodec = "avc1",
        string? acodec = null,
        double? bitrate = 2000,
        string? protocol = "https",
        bool hasDrm = false,
        string url = "",
        IReadOnlyDictionary<string, string>? headers = null) =>
        new(
            id,
            height,
            height is > 0 ? height * 16 / 9 : null,
            fps,
            vcodec,
            acodec,
            "mp4",
            bitrate,
            string.IsNullOrEmpty(url) ? $"https://cdn.example/{id}" : url,
            headers,
            protocol,
            hasDrm);

    private static StreamFormat Audio(
        string id,
        double? bitrate = 128,
        string? protocol = "https",
        bool hasDrm = false,
        string? acodec = "mp4a.40.2",
        IReadOnlyDictionary<string, string>? headers = null) =>
        new(
            id,
            null,
            null,
            null,
            null,
            acodec,
            "m4a",
            bitrate,
            $"https://cdn.example/{id}",
            headers,
            protocol,
            hasDrm);

    private static ResolvedStream BaseStream(
        IReadOnlyList<StreamFormat>? formats,
        string? audioUrl = "https://cdn.example/default-audio",
        string? audioFormatId = "140") =>
        new(
            StreamUrl: "https://cdn.example/initial-video",
            Title: "Sample",
            DurationSeconds: 120,
            WebpageUrl: "https://youtube.com/watch?v=1",
            HttpHeaders: new Dictionary<string, string> { ["User-Agent"] = "BaseUA" },
            AudioUrl: audioUrl,
            Chapters: [new StreamChapter(0, 60, "Intro")],
            Formats: formats,
            Subtitles: [new StreamSubtitle("en", "English", "https://cdn.example/en.vtt", "vtt", false)],
            VideoFormatId: "137",
            AudioFormatId: audioFormatId,
            IsLive: false);

    [Fact]
    public void TrySelect_NullOrEmptyFormats_ReturnsNull()
    {
        var opts = new StreamResolutionOptions();
        Assert.Null(StreamFormatSelector.TrySelect(BaseStream(null), opts));
        Assert.Null(StreamFormatSelector.TrySelect(BaseStream([]), opts));
    }

    [Fact]
    public void TrySelect_CustomFormatSelectorOverrideWithoutRequestedQuality_ReturnsNull()
    {
        ResolvedStream cached = BaseStream([Video("137", 1080), Audio("140")]);
        var opts = new StreamResolutionOptions(FormatSelectorOverride: "bestvideo[ext=webm]+bestaudio", RequestedQuality: null);

        Assert.Null(StreamFormatSelector.TrySelect(cached, opts));
    }

    [Fact]
    public void TrySelect_AudioOnly_UsesCachedAudioUrlWhenPresent()
    {
        ResolvedStream cached = BaseStream(
            [Video("137", 1080), Audio("140", bitrate: 128), Audio("251", bitrate: 160)],
            audioUrl: "https://cdn.example/es-audio",
            audioFormatId: "140-es");
        var opts = new StreamResolutionOptions(RequestedQuality: QualityOptions.AudioOnly);

        ResolvedStream? selected = StreamFormatSelector.TrySelect(cached, opts);

        Assert.NotNull(selected);
        Assert.Equal("https://cdn.example/es-audio", selected!.StreamUrl);
        Assert.Null(selected.AudioUrl);
        Assert.Equal("140-es", selected.VideoFormatId);
        Assert.Null(selected.AudioFormatId);
    }

    [Fact]
    public void TrySelect_AudioOnly_FallsBackToHighestBitratePlayableAudioFormat()
    {
        var audioHeaders = new Dictionary<string, string> { ["Referer"] = "https://example.com" };
        ResolvedStream cached = BaseStream(
            [
                Video("137", 1080),
                Audio("a-drm", bitrate: 500, hasDrm: true),
                Audio("a-dash", bitrate: 400, protocol: "http_dash_segments"),
                Audio("a-hls", bitrate: 192, protocol: "m3u8"),
                Audio("a-best", bitrate: 192, protocol: "https", headers: audioHeaders),
                Audio("a-low", bitrate: 96, protocol: "https"),
            ],
            audioUrl: null,
            audioFormatId: null);
        var opts = new StreamResolutionOptions(RequestedQuality: QualityOptions.AudioOnly);

        ResolvedStream? selected = StreamFormatSelector.TrySelect(cached, opts);

        Assert.NotNull(selected);
        Assert.Equal("https://cdn.example/a-best", selected!.StreamUrl);
        Assert.Equal("a-best", selected.VideoFormatId);
        Assert.Null(selected.AudioUrl);
        Assert.Null(selected.AudioFormatId);
        Assert.Same(audioHeaders, selected.HttpHeaders);
    }

    [Fact]
    public void TrySelect_AudioOnly_NoAudioAvailable_ReturnsNull()
    {
        ResolvedStream cached = BaseStream([Video("137", 1080)], audioUrl: null, audioFormatId: null);
        Assert.Null(StreamFormatSelector.TrySelect(cached, new StreamResolutionOptions(RequestedQuality: QualityOptions.AudioOnly)));
    }

    [Fact]
    public void TrySelect_SpecificHeight_RespectsMaxHeightAndPairsAudio()
    {
        ResolvedStream cached = BaseStream(
            [
                Video("v1080", 1080, bitrate: 4000),
                Video("v720", 720, bitrate: 2000),
                Video("v480", 480, bitrate: 900),
                Audio("140", bitrate: 128),
            ],
            audioUrl: null,
            audioFormatId: null);

        ResolvedStream? selected = StreamFormatSelector.TrySelect(
            cached,
            new StreamResolutionOptions(RequestedQuality: QualityOptions.ForKey("h720"), FormatSelectorOverride: "bv*[height<=720]+ba/b[height<=720]"));

        Assert.NotNull(selected);
        Assert.Equal("https://cdn.example/v720", selected!.StreamUrl);
        Assert.Equal("v720", selected.VideoFormatId);
        Assert.Equal("https://cdn.example/140", selected.AudioUrl);
        Assert.Equal("140", selected.AudioFormatId);
        Assert.Equal(cached.Chapters, selected.Chapters);
        Assert.Equal(cached.Subtitles, selected.Subtitles);
    }

    [Fact]
    public void TrySelect_AllFormatsAboveRequestedMaxHeight_ReturnsNull()
    {
        ResolvedStream cached = BaseStream([Video("v1080", 1080), Video("v720", 720), Audio("140")]);

        ResolvedStream? selected = StreamFormatSelector.TrySelect(
            cached,
            new StreamResolutionOptions(RequestedQuality: QualityOptions.ForKey("h480")));

        Assert.Null(selected);
    }

    [Fact]
    public void TrySelect_HighFps_Prefers50PlusFpsAndFallsBackWhenAbsent()
    {
        ResolvedStream with60 = BaseStream([
            Video("v1080-30", 1080, fps: 30, bitrate: 5000),
            Video("v1080-60", 1080, fps: 60, bitrate: 4000),
            Audio("140"),
        ]);

        ResolvedStream? picked60 = StreamFormatSelector.TrySelect(
            with60,
            new StreamResolutionOptions(RequestedQuality: QualityOptions.ForKey("h1080f60")));
        Assert.Equal("v1080-60", picked60?.VideoFormatId);

        ResolvedStream without60 = BaseStream([Video("v1080-30", 1080, fps: 30), Audio("140")]);
        ResolvedStream? fallback30 = StreamFormatSelector.TrySelect(
            without60,
            new StreamResolutionOptions(RequestedQuality: QualityOptions.ForKey("h1080f60")));
        Assert.Equal("v1080-30", fallback30?.VideoFormatId);
    }

    [Fact]
    public void TrySelect_PreferGpuDecodableFormats_ControlsAv1Preference()
    {
        ResolvedStream cached = BaseStream([
            Video("v1080-av1", 1080, vcodec: "av01.0.08M.08", bitrate: 6000),
            Video("v1080-h264", 1080, vcodec: "avc1.640028", bitrate: 3500),
            Audio("140"),
        ]);

        ResolvedStream? gpuPreferred = StreamFormatSelector.TrySelect(
            cached,
            new StreamResolutionOptions(PreferGpuDecodableFormats: true, RequestedQuality: QualityOptions.Auto));
        Assert.Equal("v1080-h264", gpuPreferred?.VideoFormatId);

        ResolvedStream? av1Allowed = StreamFormatSelector.TrySelect(
            cached,
            new StreamResolutionOptions(PreferGpuDecodableFormats: false, RequestedQuality: QualityOptions.Auto));
        Assert.Equal("v1080-av1", av1Allowed?.VideoFormatId);
    }

    [Fact]
    public void TrySelect_PrefersDirectHttpOverM3u8AndSkipsDrmAndBadProtocols()
    {
        ResolvedStream cached = BaseStream([
            Video("v-drm", 1080, bitrate: 9000, hasDrm: true),
            Video("v-dash", 1080, bitrate: 8000, protocol: "http_dash_segments"),
            Video("v-m3u8", 1080, bitrate: 7000, protocol: "m3u8_native"),
            Video("v-https", 1080, bitrate: 3000, protocol: "https"),
            Audio("140"),
        ]);

        ResolvedStream? selected = StreamFormatSelector.TrySelect(cached, new StreamResolutionOptions());

        Assert.Equal("v-https", selected?.VideoFormatId);
    }

    [Fact]
    public void TrySelect_MuxedFormat_ClearsAudioUrl()
    {
        ResolvedStream cached = BaseStream(
            [Video("18", 360, vcodec: "avc1", acodec: "mp4a")],
            audioUrl: "https://cdn.example/old-audio",
            audioFormatId: "140");

        ResolvedStream? selected = StreamFormatSelector.TrySelect(
            cached,
            new StreamResolutionOptions(StreamingQuality: "480p"));

        Assert.NotNull(selected);
        Assert.Equal("18", selected!.VideoFormatId);
        Assert.Null(selected.AudioUrl);
        Assert.Null(selected.AudioFormatId);
    }

    [Fact]
    public void TrySelect_VideoOnlyWithNoAudioSource_SkipsVideoOnlyAndFallsBackToMuxedOrNull()
    {
        ResolvedStream withMuxed = BaseStream(
            [
                Video("v1080-silent", 1080, vcodec: "avc1", acodec: null),
                Video("v720-muxed", 720, vcodec: "avc1", acodec: "mp4a"),
            ],
            audioUrl: null,
            audioFormatId: null);

        ResolvedStream? pickedMuxed = StreamFormatSelector.TrySelect(withMuxed, new StreamResolutionOptions());
        Assert.Equal("v720-muxed", pickedMuxed?.VideoFormatId);

        ResolvedStream onlySilent = BaseStream(
            [Video("v1080-silent", 1080, vcodec: "avc1", acodec: null)],
            audioUrl: null,
            audioFormatId: null);
        Assert.Null(StreamFormatSelector.TrySelect(onlySilent, new StreamResolutionOptions()));
    }

    [Fact]
    public void TrySelect_AutoRespectsStreamingQualitySetting()
    {
        ResolvedStream cached = BaseStream([
            Video("v2160", 2160),
            Video("v1080", 1080),
            Video("v720", 720),
            Audio("140"),
        ]);

        Assert.Equal("v720", StreamFormatSelector.TrySelect(cached, new StreamResolutionOptions(StreamingQuality: "720p", RequestedQuality: QualityOptions.Auto))?.VideoFormatId);
        Assert.Equal("v2160", StreamFormatSelector.TrySelect(cached, new StreamResolutionOptions(StreamingQuality: "best", RequestedQuality: null))?.VideoFormatId);
    }
}
