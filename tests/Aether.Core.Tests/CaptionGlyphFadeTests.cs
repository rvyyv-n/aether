using System;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class CaptionGlyphFadeTests
{
    private static readonly TimeSpan Hide = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan Show = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void StartsShown()
    {
        var fade = new CaptionGlyphFade(new ManualTimeProvider());

        Assert.Equal(1, fade.Opacity);
    }

    [Fact]
    public void Hide_ReachesZeroAtDuration_AndStops()
    {
        var time = new ManualTimeProvider();
        var fade = new CaptionGlyphFade(time);

        fade.Start(visible: false, Hide);
        Assert.True(fade.Step());
        Assert.Equal(1, fade.Opacity);

        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.True(fade.Step());
        Assert.InRange(fade.Opacity, 0, 0.5); // ease-out: most of the change happens early

        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.False(fade.Step());
        Assert.Equal(0, fade.Opacity);
    }

    [Fact]
    public void Reversal_StartsFromCurrentOpacity()
    {
        var time = new ManualTimeProvider();
        var fade = new CaptionGlyphFade(time);

        fade.Start(visible: false, Hide);
        time.Advance(TimeSpan.FromMilliseconds(30));
        fade.Step();
        double midway = fade.Opacity;
        Assert.InRange(midway, 0.01, 0.99);

        fade.Start(visible: true, Show);
        Assert.True(fade.Step());
        Assert.Equal(midway, fade.Opacity, 6);

        time.Advance(Show);
        Assert.False(fade.Step());
        Assert.Equal(1, fade.Opacity);
    }

    [Fact]
    public void ZeroDuration_JumpsImmediately()
    {
        var fade = new CaptionGlyphFade(new ManualTimeProvider());

        fade.Start(visible: false, TimeSpan.Zero);

        Assert.Equal(0, fade.Opacity);
        Assert.False(fade.Step());
        Assert.Equal(0, fade.Opacity);
    }

    [Fact]
    public void EaseOut_IsMonotonic_WithFixedEnds()
    {
        Assert.Equal(0, CaptionGlyphFade.EaseOut(0));
        Assert.Equal(1, CaptionGlyphFade.EaseOut(1));
        Assert.Equal(0, CaptionGlyphFade.EaseOut(-1));
        Assert.Equal(1, CaptionGlyphFade.EaseOut(2));

        double previous = 0;
        for (int i = 1; i <= 100; i++)
        {
            double value = CaptionGlyphFade.EaseOut(i / 100.0);
            Assert.True(value >= previous, $"EaseOut fell at t = {i / 100.0}");
            previous = value;
        }
        Assert.True(CaptionGlyphFade.EaseOut(0.5) > 0.9);
    }
}
