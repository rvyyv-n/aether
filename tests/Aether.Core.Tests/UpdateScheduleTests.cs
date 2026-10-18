using Aether.Core.Playback.Updates;

namespace Aether.Core.Tests;

public sealed class UpdateScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IsDue_WhenNeverChecked()
    {
        Assert.True(UpdateSchedule.IsDue(true, null, Now));
    }

    [Fact]
    public void IsDue_OnlyOnceADayHasPassed()
    {
        Assert.True(UpdateSchedule.IsDue(true, Now - TimeSpan.FromDays(1), Now));
        Assert.False(UpdateSchedule.IsDue(true, Now - TimeSpan.FromHours(23), Now));
    }

    [Fact]
    public void IsDue_IsNeverTrueWhileAutomaticChecksAreOff()
    {
        Assert.False(UpdateSchedule.IsDue(false, null, Now));
        Assert.False(UpdateSchedule.IsDue(false, Now - TimeSpan.FromDays(30), Now));
    }

    [Fact]
    public void IsDue_TreatsACheckInTheFutureAsStale()
    {
        Assert.True(UpdateSchedule.IsDue(true, Now + TimeSpan.FromDays(3), Now));
    }

    [Fact]
    public void DueIn_CountsDownToTheNextCheck()
    {
        Assert.Equal(TimeSpan.FromHours(5), UpdateSchedule.DueIn(Now - TimeSpan.FromHours(19), Now));
        Assert.Equal(TimeSpan.Zero, UpdateSchedule.DueIn(Now - TimeSpan.FromDays(2), Now));
        Assert.Equal(TimeSpan.Zero, UpdateSchedule.DueIn(null, Now));
        Assert.Equal(TimeSpan.Zero, UpdateSchedule.DueIn(Now + TimeSpan.FromHours(1), Now));
    }

    [Fact]
    public void CanCheck_IsFalseWhileBusyOrWithoutAnInstaller()
    {
        Assert.True(UpdateStatus.Idle.CanCheck);
        Assert.True(new UpdateStatus(UpdateState.Failed).CanCheck);
        Assert.True(new UpdateStatus(UpdateState.Ready, "1.1.0").CanCheck);
        Assert.False(new UpdateStatus(UpdateState.Checking).CanCheck);
        Assert.False(new UpdateStatus(UpdateState.Downloading, "1.1.0", 0.4).CanCheck);
        Assert.False(new UpdateStatus(UpdateState.NotInstalled).CanCheck);
    }
}
