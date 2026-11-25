using Aether.Core.Playback;
using Aether.Core.Playback.MediaIntegration;

namespace Aether.Core.Tests.Playback;

public sealed class SmtcStateTests
{
    [Fact]
    public void Metadata_IsEmpty_WhenNoTitleOrPath()
    {
        SmtcMetadata meta = SmtcMetadata.From(null, null, null);
        Assert.False(meta.HasMedia);
        Assert.Equal(string.Empty, meta.Title);
        Assert.Equal(string.Empty, meta.Subtitle);
    }

    [Fact]
    public void Metadata_UsesFileNameWithoutExtension_AndParentFolder_ForLocalFiles()
    {
        SmtcMetadata meta = SmtcMetadata.From(null, @"D:\Videos\Valorant\clutch-round-12.mp4", null);
        Assert.True(meta.HasMedia);
        Assert.Equal("clutch-round-12", meta.Title);
        Assert.Equal("Valorant", meta.Subtitle);
    }

    [Fact]
    public void Metadata_UsesStreamTitle_AndStrippedHost_ForWebLinks()
    {
        SmtcMetadata meta = SmtcMetadata.From("Aether Demo", "https://cdn.example.com/stream.m3u8", "https://www.youtube.com/watch?v=abc123");
        Assert.True(meta.HasMedia);
        Assert.Equal("Aether Demo", meta.Title);
        Assert.Equal("youtube.com", meta.Subtitle);
    }

    [Fact]
    public void Metadata_FallsBackToAppName_WhenNoParentOrHost()
    {
        SmtcMetadata meta = SmtcMetadata.From("Live Event", null, "not-a-url", "Aether");
        Assert.True(meta.HasMedia);
        Assert.Equal("Live Event", meta.Title);
        Assert.Equal("Aether", meta.Subtitle);
    }

    [Theory]
    [InlineData(PlaybackStatus.Idle, false, false, false, false, SmtcPlaybackState.Closed)]
    [InlineData(PlaybackStatus.Loading, true, false, false, true, SmtcPlaybackState.Changing)]
    [InlineData(PlaybackStatus.Playing, true, true, true, true, SmtcPlaybackState.Playing)]
    [InlineData(PlaybackStatus.Paused, true, true, true, true, SmtcPlaybackState.Paused)]
    [InlineData(PlaybackStatus.Ended, true, true, false, false, SmtcPlaybackState.Stopped)]
    public void ButtonState_MapsPlaybackStatus(
        PlaybackStatus status,
        bool enabled,
        bool play,
        bool pause,
        bool stop,
        SmtcPlaybackState expected)
    {
        SmtcButtonState state = SmtcButtonState.From(status, hasPrevious: true, hasNext: false);
        Assert.Equal(enabled, state.IsEnabled);
        Assert.Equal(play, state.IsPlayEnabled);
        Assert.Equal(pause, state.IsPauseEnabled);
        Assert.Equal(stop, state.IsStopEnabled);
        Assert.Equal(expected, state.PlaybackStatus);
        Assert.Equal(enabled, state.IsPreviousEnabled);
        Assert.False(state.IsNextEnabled);
    }

    [Fact]
    public void Timeline_ClampsPosition_AndDisablesSeekForLive()
    {
        SmtcTimeline normal = SmtcTimeline.From(45.5, 120.0, isLive: false);
        Assert.True(normal.IsSeekable);
        Assert.Equal(TimeSpan.FromSeconds(45.5), normal.Position);
        Assert.Equal(TimeSpan.FromSeconds(120.0), normal.EndTime);
        Assert.Equal(TimeSpan.FromSeconds(120.0), normal.MaxSeekTime);

        SmtcTimeline clamped = SmtcTimeline.From(999, 60, isLive: false);
        Assert.Equal(TimeSpan.FromSeconds(60), clamped.Position);

        SmtcTimeline live = SmtcTimeline.From(12, 120, isLive: true);
        Assert.False(live.IsSeekable);
        Assert.Equal(TimeSpan.Zero, live.EndTime);
    }

    [Fact]
    public void Timeline_ShouldPush_ThrottlesSteadyPlayback_AndPushesOnSeekOrInterval()
    {
        SmtcTimeline t0 = SmtcTimeline.From(10.0, 120.0, isLive: false);
        SmtcTimeline t1 = SmtcTimeline.From(10.5, 120.0, isLive: false);
        Assert.False(SmtcTimeline.ShouldPush(t0, t1, elapsedMs: 500));

        SmtcTimeline t5 = SmtcTimeline.From(15.0, 120.0, isLive: false);
        Assert.True(SmtcTimeline.ShouldPush(t0, t5, elapsedMs: 5000));

        // User seeked forward 20s in 100ms -> push immediately.
        SmtcTimeline seeked = SmtcTimeline.From(30.0, 120.0, isLive: false);
        Assert.True(SmtcTimeline.ShouldPush(t0, seeked, elapsedMs: 100));

        // Duration changed -> push immediately.
        SmtcTimeline newFile = SmtcTimeline.From(0.0, 300.0, isLive: false);
        Assert.True(SmtcTimeline.ShouldPush(t0, newFile, elapsedMs: 50));
    }
}
