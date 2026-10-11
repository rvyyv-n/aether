using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class OpenUrlFlowTests
{
    [Theory]
    [InlineData("https://youtu.be/8kq2Xb3fZ1w", true)]
    [InlineData("http://example.com/video.mp4", true)]
    [InlineData("  HTTPS://Example.com  ", true)]
    [InlineData("notes for tonight", false)]
    [InlineData("youtube.com/watch?v=1", false)]
    [InlineData("ftp://example.com/a.mp4", false)]
    [InlineData("https://", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void WebLink_AcceptsOnlyHttpAndHttps(string? text, bool valid) => Assert.Equal(valid, WebLink.IsValid(text));

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=1", "youtube.com")]
    [InlineData("https://youtu.be/8kq2Xb3fZ1w", "youtu.be")]
    [InlineData("notes", null)]
    public void WebLink_HostDropsWww(string text, string? host) => Assert.Equal(host, WebLink.Host(text));

    [Fact]
    public void Open_PrefillsOnlyAWebLinkFromTheClipboard()
    {
        var flow = new OpenUrlFlow(new ManualTimeProvider(), streamingAvailable: false);

        flow.Open(" https://youtu.be/x \r\n");
        Assert.Equal(OpenUrlStage.Editing, flow.Stage);
        Assert.Equal("https://youtu.be/x", flow.Text);
        Assert.True(flow.CanSubmit);

        flow.Open("shopping list");
        Assert.Equal(string.Empty, flow.Text);
        Assert.False(flow.CanSubmit);
        Assert.Equal(OpenUrlNote.Hint, flow.Note); // empty is not invalid
    }

    [Fact]
    public void TypingSomethingElse_ShowsInvalidAndDisablesPlay()
    {
        var flow = new OpenUrlFlow(new ManualTimeProvider(), streamingAvailable: true);
        flow.Open(null);

        flow.SetText("notes for tonight");

        Assert.Equal(OpenUrlNote.Invalid, flow.Note);
        Assert.False(flow.CanSubmit);
        Assert.Equal(OpenUrlSubmitResult.Rejected, flow.Submit());
        Assert.Equal(OpenUrlStage.Editing, flow.Stage);
    }

    [Fact]
    public void WithoutStreaming_PlayKeepsTheDialogOpenAndSaysSo()
    {
        var flow = new OpenUrlFlow(new ManualTimeProvider(), streamingAvailable: false);
        flow.Open("https://youtu.be/x");

        Assert.Equal(OpenUrlSubmitResult.Unavailable, flow.Submit());
        Assert.Equal(OpenUrlStage.Editing, flow.Stage);
        Assert.Equal(OpenUrlNote.Unavailable, flow.Note);
        Assert.Null(flow.DueIn);

        flow.SetText("https://youtu.be/y");
        Assert.Equal(OpenUrlNote.Hint, flow.Note);
    }

    [Fact]
    public void GettingStream_BecomesStillWorkingAfterTenSeconds()
    {
        var time = new ManualTimeProvider();
        var flow = new OpenUrlFlow(time, streamingAvailable: true);
        flow.Open("https://www.youtube.com/watch?v=1");

        Assert.Equal(OpenUrlSubmitResult.Started, flow.Submit());
        Assert.Equal(OpenUrlStage.GettingStream, flow.Stage);
        Assert.Equal("youtube.com", flow.Host);
        Assert.Equal(OpenUrlFlow.StillWorkingAfter, flow.DueIn);

        time.Advance(TimeSpan.FromSeconds(9));
        flow.Tick();
        Assert.Equal(OpenUrlStage.GettingStream, flow.Stage);
        Assert.Equal(TimeSpan.FromSeconds(1), flow.DueIn);

        time.Advance(TimeSpan.FromSeconds(1));
        flow.Tick();
        Assert.Equal(OpenUrlStage.StillWorking, flow.Stage);
        Assert.Null(flow.DueIn);
    }

    [Fact]
    public void Cancel_ClosesFromAnyStage()
    {
        var flow = new OpenUrlFlow(new ManualTimeProvider(), streamingAvailable: true);
        flow.Open("https://youtu.be/x");
        flow.Submit();

        flow.Close();

        Assert.Equal(OpenUrlStage.Closed, flow.Stage);
        Assert.Null(flow.Link);
        Assert.Null(flow.DueIn);
    }
}
