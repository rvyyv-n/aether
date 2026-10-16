using Aether.Core.Playback.Windowing;
using Xunit;

namespace Aether.Core.Tests;

public class WindowStateStorePipTests
{
    [Fact]
    public void SerializeAndParse_WithPictureInPicture_RoundTrips()
    {
        var original = new SavedWindow(
            new WindowRect(100, 200, 1280, 720),
            IsMaximized: false,
            PictureInPicture: new WindowRect(300, 400, 416, 233));

        string json = WindowStateStore.Serialize(original);
        SavedWindow? parsed = WindowStateStore.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(original, parsed);
        Assert.Equal(original.PictureInPicture, parsed.PictureInPicture);
    }

    [Fact]
    public void Parse_WithoutPipProperty_LoadsWindowWithNullPictureInPicture()
    {
        // Older versions of window.json had no "pip" object
        string json = """
        {
          "version": 1,
          "x": 100,
          "y": 200,
          "width": 1280,
          "height": 720,
          "maximized": true
        }
        """;

        SavedWindow? parsed = WindowStateStore.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(new WindowRect(100, 200, 1280, 720), parsed.Bounds);
        Assert.True(parsed.IsMaximized);
        Assert.Null(parsed.PictureInPicture);
    }

    [Theory]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":{"x":10,"y":20,"width":99,"height":200}}""")]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":{"x":10,"y":20,"width":200,"height":99}}""")]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":"not-an-object"}""")]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":123}""")]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":true}""")]
    [InlineData("""{"version":1,"x":100,"y":200,"width":800,"height":600,"maximized":false,"pip":[]}""")]
    public void Parse_InvalidOrTooSmallPip_ReturnsNullPipAndPreservesMainBounds(string json)
    {
        SavedWindow? parsed = WindowStateStore.Parse(json);

        Assert.NotNull(parsed);
        Assert.Equal(new WindowRect(100, 200, 800, 600), parsed.Bounds);
        Assert.False(parsed.IsMaximized);
        Assert.Null(parsed.PictureInPicture);
    }
}
