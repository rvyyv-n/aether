using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public sealed class YtDlpUpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsDue_WhenNeverChecked()
    {
        Assert.True(YtDlpUpdateSchedule.IsDue(true, null, Now));
    }

    [Fact]
    public void IsDue_OnceADayHasPassed()
    {
        Assert.True(YtDlpUpdateSchedule.IsDue(true, Now - TimeSpan.FromDays(1), Now));
        Assert.False(YtDlpUpdateSchedule.IsDue(true, Now - TimeSpan.FromHours(23), Now));
    }

    [Fact]
    public void IsDue_IsFalseWhenAutomaticUpdatesAreOff()
    {
        Assert.False(YtDlpUpdateSchedule.IsDue(false, null, Now));
        Assert.False(YtDlpUpdateSchedule.IsDue(false, Now - TimeSpan.FromDays(30), Now));
    }

    [Fact]
    public void IsDue_TreatsACheckInTheFutureAsStale()
    {
        Assert.True(YtDlpUpdateSchedule.IsDue(true, Now + TimeSpan.FromDays(3), Now));
    }

    [Fact]
    public void DueIn_CountsDownToTheNextCheck()
    {
        Assert.Equal(TimeSpan.FromHours(5), YtDlpUpdateSchedule.DueIn(Now - TimeSpan.FromHours(19), Now));
        Assert.Equal(TimeSpan.Zero, YtDlpUpdateSchedule.DueIn(Now - TimeSpan.FromDays(2), Now));
        Assert.Equal(TimeSpan.Zero, YtDlpUpdateSchedule.DueIn(null, Now));
    }
}
