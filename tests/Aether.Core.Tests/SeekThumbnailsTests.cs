using Aether.Core.Playback.Streams;
using Aether.Core.Playback.Thumbnails;
using Xunit;

namespace Aether.Core.Tests;

public class SeekThumbnailsTests
{
    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(60, 1.0)]
    [InlineData(120, 1.0)]
    [InlineData(300, 2.0)]
    [InlineData(600, 2.0)]
    [InlineData(1800, 5.0)]
    [InlineData(3600, 5.0)]
    [InlineData(7200, 20.0)]
    public void ComputeBucketSeconds_Scales_With_Duration(double duration, double expectedStep)
    {
        Assert.Equal(expectedStep, ThumbnailTiming.ComputeBucketSeconds(duration));
    }

    [Fact]
    public void QuantizeBucket_Clamps_Near_End_And_Tracks_Buckets()
    {
        Assert.Equal((0, 0.0), ThumbnailTiming.QuantizeBucket(-5, 90));
        Assert.Equal((15, 15.0), ThumbnailTiming.QuantizeBucket(15.6, 90));

        (int lastBucket, double lastTarget) = ThumbnailTiming.QuantizeBucket(95, 90);
        Assert.Equal(89, lastBucket);
        Assert.InRange(lastTarget, 88.9, 89.96);
    }

    [Theory]
    [InlineData(1920, 1080, 192, 108)]
    [InlineData(1080, 1920, 68, 120)]
    [InlineData(2560, 1080, 192, 80)]
    [InlineData(0, 0, 192, 108)]
    public void FitThumbnailSize_Preserves_Aspect_With_Even_Dimensions(int srcW, int srcH, int expectedW, int expectedH)
    {
        (int width, int height) = ThumbnailTiming.FitThumbnailSize(srcW, srcH);
        Assert.Equal(expectedW, width);
        Assert.Equal(expectedH, height);
        Assert.Equal(0, width % 2);
        Assert.Equal(0, height % 2);
    }

    [Fact]
    public void ThumbnailLruCache_Promotes_Hits_And_Evicts_Oldest()
    {
        var cache = new ThumbnailLruCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(2, "two");
        Assert.True(cache.TryGet(1, out string hit));
        Assert.Equal("one", hit);

        cache.Set(3, "three");
        Assert.False(cache.TryGet(2, out _));
        Assert.True(cache.TryGet(1, out _));
        Assert.True(cache.TryGet(3, out _));

        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void StoryboardIndex_Selects_Preferred_Width_Or_Fallback_Largest()
    {
        var small = new StreamStoryboard("sb2", 80, 45, 10, 10, 0.5, [new("https://i.ytimg.com/s.jpg", 50)]);
        var medium = new StreamStoryboard("sb1", 160, 90, 5, 5, 0.5, [new("https://i.ytimg.com/m.jpg", 50)]);
        var large = new StreamStoryboard("sb0", 320, 180, 3, 3, 0.5, [new("https://i.ytimg.com/l.jpg", 50)]);

        StreamStoryboard? picked = StoryboardIndex.SelectBest([small, large, medium], preferredWidth: 160);
        Assert.Same(medium, picked);

        StreamStoryboard? fallback = StoryboardIndex.SelectBest([small], preferredWidth: 160);
        Assert.Same(small, fallback);
    }

    [Fact]
    public void StoryboardIndex_Locates_MultiFragment_And_Partial_Final_Sheet_Tiles()
    {
        // 5x5 tiles at 2s each => full sheet covers 50s; final fragment is partial (30s = 15 tiles).
        var storyboard = new StreamStoryboard(
            FormatId: "sb1",
            TileWidth: 160,
            TileHeight: 90,
            Rows: 5,
            Columns: 5,
            Fps: 0.5,
            Fragments:
            [
                new("https://i.ytimg.com/M0.jpg", 50.0),
                new("https://i.ytimg.com/M1.jpg", 30.0),
            ]);

        // 14s => sheet 0, tile 7 (row 1, col 2)
        StoryboardTile? tile0 = StoryboardIndex.Locate(storyboard, 14.2);
        Assert.NotNull(tile0);
        Assert.Equal(0, tile0!.FragmentIndex);
        Assert.Equal(2 * 160, tile0.CropX);
        Assert.Equal(1 * 90, tile0.CropY);

        // 55s => 5s into sheet 1 => tile 2 (row 0, col 2), not stretched across 25 tiles
        StoryboardTile? tile1 = StoryboardIndex.Locate(storyboard, 55.0);
        Assert.NotNull(tile1);
        Assert.Equal(1, tile1!.FragmentIndex);
        Assert.Equal("https://i.ytimg.com/M1.jpg", tile1.SheetUrl);
        Assert.Equal(2 * 160, tile1.CropX);
        Assert.Equal(0, tile1.CropY);

        // Past total duration clamps cleanly to last usable tile in fragment 1
        StoryboardTile? clamped = StoryboardIndex.Locate(storyboard, 999.0);
        Assert.NotNull(clamped);
        Assert.Equal(1, clamped!.FragmentIndex);
        Assert.Equal(4 * 160, clamped.CropX);
        Assert.Equal(2 * 90, clamped.CropY);
    }

    [Fact]
    public void YtDlpStreamResolver_Extracts_Storyboards_Without_Polluting_Formats()
    {
        const string json = """
            {
              "title": "Demo",
              "url": "https://cdn.example/video.mp4",
              "formats": [
                {
                  "format_id": "137",
                  "ext": "mp4",
                  "height": 1080,
                  "width": 1920,
                  "vcodec": "avc1",
                  "acodec": "none",
                  "url": "https://cdn.example/1080.mp4"
                },
                {
                  "format_id": "sb1",
                  "format_note": "storyboard",
                  "ext": "mhtml",
                  "protocol": "mhtml",
                  "width": 160,
                  "height": 90,
                  "rows": 5,
                  "columns": 5,
                  "fps": 0.5,
                  "http_headers": { "User-Agent": "TestAgent" },
                  "fragments": [
                    { "url": "https://i.ytimg.com/sb/abc/M0.jpg", "duration": 50.0 },
                    { "url": "javascript:alert(1)", "duration": 10.0 }
                  ]
                },
                {
                  "format_id": "sb_bad",
                  "format_note": "storyboard",
                  "ext": "mhtml",
                  "width": 0,
                  "height": 90,
                  "rows": 5,
                  "columns": 5,
                  "fragments": [ { "url": "https://i.ytimg.com/sb/abc/bad.jpg", "duration": 5.0 } ]
                }
              ]
            }
            """;

        ResolvedStream resolved = YtDlpStreamResolver.ParseStreamJson(json.ReplaceLineEndings(" "), "https://youtube.com/watch?v=abc");

        Assert.NotNull(resolved.Formats);
        Assert.Single(resolved.Formats!);
        Assert.Equal("137", resolved.Formats![0].FormatId);

        Assert.NotNull(resolved.Storyboards);
        StreamStoryboard sb = Assert.Single(resolved.Storyboards!);
        Assert.Equal("sb1", sb.FormatId);
        Assert.Equal(160, sb.TileWidth);
        Assert.Equal(90, sb.TileHeight);
        Assert.Equal(5, sb.Rows);
        Assert.Equal(5, sb.Columns);
        Assert.Equal(0.5, sb.Fps);
        Assert.Equal("TestAgent", sb.HttpHeaders?["User-Agent"]);
        StoryboardFragment frag = Assert.Single(sb.Fragments);
        Assert.Equal("https://i.ytimg.com/sb/abc/M0.jpg", frag.Url);
        Assert.Equal(50.0, frag.Duration);
    }
}
