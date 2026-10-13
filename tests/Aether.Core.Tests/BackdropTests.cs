using System;
using System.Diagnostics;
using Aether.Core.Interop.Rendering;
using Xunit;

namespace Aether.Core.Tests;

public class BackdropTests
{
    private static BackdropRegion Card(float x = 100, float y = 200, float w = 520, float h = 84, uint surfaceW = 720, uint surfaceH = 400) =>
        new(x, y, w, h, 24, 1f, surfaceW, surfaceH);

    private static long Ms(double milliseconds) => (long)(milliseconds * Stopwatch.Frequency / 1000.0);

    [Fact]
    public void Layout_KeepsUiRows_SinceGlRowZeroIsTheTop()
    {
        Assert.True(BackdropGeometry.TryLayout(Card(), 720, 400, out BackdropLayout l));

        Assert.Equal(200f, l.CardY0);
        Assert.Equal(284f, l.CardY1);
        Assert.Equal(100f, l.CardX0);
        Assert.Equal(620f, l.CardX1);
    }

    [Fact]
    public void Layout_CopiesCardPlusMargin_ClampedToTheBuffer()
    {
        Assert.True(BackdropGeometry.TryLayout(Card(), 720, 400, out BackdropLayout l));

        Assert.Equal(76, l.CopyX);
        Assert.Equal(176, l.CopyY);
        Assert.Equal(568, l.CopyWidth);
        Assert.Equal(132, l.CopyHeight);

        Assert.True(BackdropGeometry.TryLayout(Card(x: 4, y: 330, surfaceH: 400), 720, 400, out BackdropLayout edge));
        Assert.Equal(0, edge.CopyX);
        Assert.Equal(400, edge.CopyY + edge.CopyHeight); // card runs past the bottom: the copy stops at the last row
    }

    [Fact]
    public void Layout_RescalesWhenTheBufferIsAlreadyANewerSize()
    {
        // Posted against 720×400, rendered into 1440×800.
        Assert.True(BackdropGeometry.TryLayout(Card(), 1440, 800, out BackdropLayout l));

        Assert.Equal(200f, l.CardX0);
        Assert.Equal(1240f, l.CardX1);
        Assert.Equal(400f, l.CardY0);
        Assert.Equal(568f, l.CardY1);
        Assert.Equal(48f, l.Radius);
        Assert.Equal(2f, l.Scale);
    }

    [Theory]
    [InlineData(0f, 84f)]
    [InlineData(520f, 0f)]
    [InlineData(float.NaN, 84f)]
    public void Layout_EmptyOrInvalidCard_DrawsNothing(float width, float height)
    {
        Assert.False(BackdropGeometry.TryLayout(Card(w: width, h: height), 720, 400, out _));
    }

    [Fact]
    public void Layout_CardOutsideTheBuffer_DrawsNothing()
    {
        Assert.False(BackdropGeometry.TryLayout(Card(x: 2000), 720, 400, out _));
        Assert.False(BackdropGeometry.TryLayout(Card(), 0, 400, out _));
    }

    [Fact]
    public void FadeOpacity_ZeroDuration_IsInstant()
    {
        Assert.Equal(0f, BackdropGeometry.FadeOpacity(1f, 0f, 0, 0));
        Assert.Equal(1f, BackdropGeometry.FadeOpacity(0f, 1f, -5, 0));
    }

    [Fact]
    public void FadeOpacity_EasesOutAndLandsOnTheTarget()
    {
        Assert.Equal(1f, BackdropGeometry.FadeOpacity(1f, 0f, 0, 300));
        float half = BackdropGeometry.FadeOpacity(1f, 0f, 150, 300);
        Assert.InRange(half, 0.01f, 0.5f); // ease-out: most of the change happens early
        Assert.Equal(0f, BackdropGeometry.FadeOpacity(1f, 0f, 300, 300));
        Assert.Equal(0f, BackdropGeometry.FadeOpacity(1f, 0f, 900, 300));
    }

    [Fact]
    public void Source_StartsShown_ButOffUntilActivated()
    {
        var source = new BackdropSource();
        source.Post(Card());

        Assert.False(source.TryGetFrame(Ms(0), out _));

        source.SetActive(true);
        Assert.True(source.TryGetFrame(Ms(0), out BackdropFrame frame));
        Assert.Equal(1f, frame.Opacity);
        Assert.Equal(Card(), frame.Region);
    }

    [Fact]
    public void Source_NoCardPosted_DrawsNothing()
    {
        var source = new BackdropSource();
        source.SetActive(true);

        Assert.False(source.TryGetFrame(Ms(0), out _));
    }

    [Fact]
    public void Source_HideFade_KeepsAskingForFramesUntilTheLastOneIsDrawn()
    {
        var source = new BackdropSource();
        source.Post(Card());
        source.SetActive(true);

        source.Fade(visible: false, durationMilliseconds: 300, Ms(1000));
        Assert.True(source.NeedsFrameAt(Ms(1000)));

        Assert.True(source.TryGetFrame(Ms(1100), out BackdropFrame mid));
        Assert.InRange(mid.Opacity, 0.01f, 0.99f);
        Assert.True(source.NeedsFrameAt(Ms(1100)));

        // Time has passed the end, but the frame at 0 has not been drawn yet.
        Assert.True(source.NeedsFrameAt(Ms(1310)));
        Assert.False(source.TryGetFrame(Ms(1310), out BackdropFrame last));
        Assert.Equal(0f, last.Opacity);
        Assert.False(source.NeedsFrameAt(Ms(1311)));
    }

    [Fact]
    public void Source_FadeNobodyRenders_StopsAskingAfterAGraceTime()
    {
        var source = new BackdropSource();
        source.Fade(visible: false, durationMilliseconds: 300, Ms(0));

        Assert.True(source.NeedsFrameAt(Ms(350)));
        Assert.False(source.NeedsFrameAt(Ms(500)));
    }

    [Fact]
    public void Source_ReducedMotion_JumpsWithoutRequestingFrames()
    {
        var source = new BackdropSource();
        source.Post(Card());
        source.SetActive(true);

        source.Fade(visible: false, durationMilliseconds: 0, Ms(1000));

        Assert.False(source.NeedsFrameAt(Ms(1000)));
        Assert.False(source.TryGetFrame(Ms(1000), out _));
    }

    [Fact]
    public void Source_ReversingMidFade_StartsFromTheCurrentOpacity()
    {
        var source = new BackdropSource();
        source.Post(Card());
        source.SetActive(true);

        source.Fade(visible: false, durationMilliseconds: 300, Ms(0));
        source.TryGetFrame(Ms(60), out BackdropFrame before);
        source.Fade(visible: true, durationMilliseconds: 150, Ms(60));
        source.TryGetFrame(Ms(60), out BackdropFrame after);

        Assert.InRange(before.Opacity, 0.01f, 0.99f);
        Assert.Equal(before.Opacity, after.Opacity, 4);

        source.TryGetFrame(Ms(210), out BackdropFrame done);
        Assert.Equal(1f, done.Opacity);
    }
}
