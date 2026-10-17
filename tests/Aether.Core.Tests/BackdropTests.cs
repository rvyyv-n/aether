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

    private const BackdropSlot Bar = BackdropSlot.Bar;
    private const BackdropSlot Sidebar = BackdropSlot.Sidebar;

    private static int Frames(BackdropSource source, long timestamp, out BackdropFrame first)
    {
        Span<BackdropFrame> frames = stackalloc BackdropFrame[BackdropSource.SlotCount];
        int count = source.TryGetFrames(timestamp, frames);
        first = count > 0 ? frames[0] : default;
        return count;
    }

    [Fact]
    public void Source_StartsShown_ButOffUntilActivated()
    {
        var source = new BackdropSource();
        source.Post(Bar, Card());

        Assert.Equal(0, Frames(source, Ms(0), out _));

        source.SetActive(Bar, true);
        Assert.Equal(1, Frames(source, Ms(0), out BackdropFrame frame));
        Assert.Equal(1f, frame.Opacity);
        Assert.Equal(Card(), frame.Region);
        Assert.Equal((int)Bar, frame.Slot);
    }

    [Fact]
    public void Source_NoCardPosted_DrawsNothing()
    {
        var source = new BackdropSource();
        source.SetActive(Bar, true);

        Assert.Equal(0, Frames(source, Ms(0), out _));
    }

    [Fact]
    public void Source_HideFade_KeepsAskingForFramesUntilTheLastOneIsDrawn()
    {
        var source = new BackdropSource();
        source.Post(Bar, Card());
        source.SetActive(Bar, true);

        source.Fade(Bar, visible: false, durationMilliseconds: 300, Ms(1000));
        Assert.True(source.NeedsFrameAt(Ms(1000)));

        Assert.Equal(1, Frames(source, Ms(1100), out BackdropFrame mid));
        Assert.InRange(mid.Opacity, 0.01f, 0.99f);
        Assert.True(source.NeedsFrameAt(Ms(1100)));

        // Time has passed the end, but the frame at 0 has not been drawn yet.
        Assert.True(source.NeedsFrameAt(Ms(1310)));
        Assert.Equal(0, Frames(source, Ms(1310), out _));
        Assert.False(source.NeedsFrameAt(Ms(1311)));
    }

    [Fact]
    public void Source_FadeNobodyRenders_StopsAskingAfterAGraceTime()
    {
        var source = new BackdropSource();
        source.Fade(Bar, visible: false, durationMilliseconds: 300, Ms(0));

        Assert.True(source.NeedsFrameAt(Ms(350)));
        Assert.False(source.NeedsFrameAt(Ms(500)));
    }

    [Fact]
    public void Source_ReducedMotion_JumpsWithoutRequestingFrames()
    {
        var source = new BackdropSource();
        source.Post(Bar, Card());
        source.SetActive(Bar, true);

        source.Fade(Bar, visible: false, durationMilliseconds: 0, Ms(1000));

        Assert.False(source.NeedsFrameAt(Ms(1000)));
        Assert.Equal(0, Frames(source, Ms(1000), out _));
    }

    [Fact]
    public void Source_ReversingMidFade_StartsFromTheCurrentOpacity()
    {
        var source = new BackdropSource();
        source.Post(Bar, Card());
        source.SetActive(Bar, true);

        source.Fade(Bar, visible: false, durationMilliseconds: 300, Ms(0));
        Frames(source, Ms(60), out BackdropFrame before);
        source.Fade(Bar, visible: true, durationMilliseconds: 150, Ms(60));
        Frames(source, Ms(60), out BackdropFrame after);

        Assert.InRange(before.Opacity, 0.01f, 0.99f);
        Assert.Equal(before.Opacity, after.Opacity, 4);

        Frames(source, Ms(210), out BackdropFrame done);
        Assert.Equal(1f, done.Opacity);
    }

    [Fact]
    public void Source_TwoSlots_FadeOnTheirOwn()
    {
        var source = new BackdropSource();
        source.Post(Bar, Card());
        source.Post(Sidebar, Card(x: 16, w: 48, h: 168));
        source.SetActive(Bar, true);
        source.SetActive(Sidebar, true);
        source.Fade(Sidebar, visible: false, durationMilliseconds: 0, Ms(0));

        // Only the bar draws while the sidebar is faded out.
        Assert.Equal(1, Frames(source, Ms(0), out BackdropFrame barOnly));
        Assert.Equal((int)Bar, barOnly.Slot);

        // Fading the sidebar in leaves the bar's opacity alone.
        source.Fade(Sidebar, visible: true, durationMilliseconds: 200, Ms(1000));
        Span<BackdropFrame> frames = stackalloc BackdropFrame[BackdropSource.SlotCount];
        Assert.Equal(2, source.TryGetFrames(Ms(1100), frames));
        Assert.Equal(1f, frames[0].Opacity);
        Assert.Equal((int)Sidebar, frames[1].Slot);
        Assert.InRange(frames[1].Opacity, 0.01f, 0.99f);
        Assert.Equal(48f, frames[1].Region.Width);
    }

    [Fact]
    public void Source_NeedsFrame_WhileEitherSlotIsFading()
    {
        var source = new BackdropSource();
        source.Fade(Bar, visible: true, durationMilliseconds: 0, Ms(0));
        source.Fade(Sidebar, visible: true, durationMilliseconds: 0, Ms(0));
        Assert.False(source.NeedsFrameAt(Ms(10)));

        source.Fade(Sidebar, visible: false, durationMilliseconds: 200, Ms(1000));
        Assert.True(source.NeedsFrameAt(Ms(1100)));

        // Drawing the last frame of the sidebar's fade ends the request, whatever the bar is doing.
        Span<BackdropFrame> frames = stackalloc BackdropFrame[BackdropSource.SlotCount];
        source.TryGetFrames(Ms(1250), frames);
        Assert.False(source.NeedsFrameAt(Ms(1251)));

        source.Fade(Bar, visible: false, durationMilliseconds: 200, Ms(2000));
        Assert.True(source.NeedsFrameAt(Ms(2100)));
    }

    [Fact]
    public void Source_InactiveSlot_DrawsNothingEvenWhenShown()
    {
        var source = new BackdropSource();
        source.Post(Sidebar, Card(x: 16, w: 48, h: 168));
        source.SetActive(Bar, true);

        Assert.Equal(0, Frames(source, Ms(0), out _));

        source.SetActive(Sidebar, true);
        Assert.Equal(1, Frames(source, Ms(0), out BackdropFrame frame));
        Assert.Equal((int)Sidebar, frame.Slot);
    }
}
