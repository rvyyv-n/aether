using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class ToastQueueTests
{
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly ManualTimeProvider _time = new();
    private readonly ToastQueue _queue;
    private int _changes;

    public ToastQueueTests()
    {
        _queue = new ToastQueue(_time);
        _queue.Changed += (_, _) => _changes++;
    }

    private static ToastRequest Plain(string key) => new(key, key);

    private static ToastRequest Failure(string key) => new(key, key, ToastTone.Failure);

    private static ToastRequest WithAction(string key) => new(key, key, ActionLabel: "Open", Action: () => { });

    private void Pass(TimeSpan span)
    {
        _time.Advance(span);
        _queue.Tick();
    }

    [Fact]
    public void ShowsOneAtATimeAndTheRestWaitInOrder()
    {
        _queue.Show(Plain("a"));
        _queue.Show(Plain("b"));
        _queue.Show(Plain("c"));

        Assert.Equal("a", _queue.Showing?.Key);
        Assert.Equal(2, _queue.WaitingCount);

        Pass(ToastQueue.DefaultDuration);
        Assert.Equal("b", _queue.Showing?.Key);
        Pass(ToastQueue.DefaultDuration);
        Assert.Equal("c", _queue.Showing?.Key);
        Pass(ToastQueue.DefaultDuration);
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void FailuresGoToTheFrontOfTheQueueButNotAheadOfEarlierFailures()
    {
        _queue.Show(Plain("shown"));
        _queue.Show(Plain("late"));
        _queue.Show(Failure("fail1"));
        _queue.Show(Failure("fail2"));

        var order = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            order.Add(_queue.Showing!.Key);
            Pass(ToastQueue.DefaultDuration);
        }

        Assert.Equal(["shown", "fail1", "fail2", "late"], order);
    }

    [Fact]
    public void AToastWithAnActionIsNeverReplacedAndRunsItsFullTime()
    {
        _queue.Show(WithAction("clip"));
        _queue.Show(Failure("fail"));

        Pass(ToastQueue.DefaultDuration + Second);
        Assert.Equal("clip", _queue.Showing?.Key);

        Pass(ToastQueue.DefaultActionDuration - ToastQueue.DefaultDuration - Second);
        Assert.Equal("fail", _queue.Showing?.Key);
    }

    [Fact]
    public void ARepeatOfTheToastOnScreenShowsOnceAndRestartsItsTimer()
    {
        _queue.Show(Plain("copied"));
        Pass(3 * Second);
        _queue.Show(Plain("copied"));

        Assert.Equal(0, _queue.WaitingCount);
        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
        Pass(3 * Second);
        Assert.Equal("copied", _queue.Showing?.Key);
        Pass(Second);
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void ARepeatOfAWaitingToastDoesNotQueueItTwice()
    {
        _queue.Show(Plain("a"));
        _queue.Show(Plain("b"));
        _queue.Show(Plain("b"));

        Assert.Equal(1, _queue.WaitingCount);
    }

    [Fact]
    public void LastsFourSecondsOrEightWithAnAction()
    {
        _queue.Show(Plain("a"));
        Assert.Equal(TimeSpan.FromSeconds(4), _queue.DueIn);
        _queue.Dismiss();

        _queue.Show(WithAction("b"));
        Assert.Equal(TimeSpan.FromSeconds(8), _queue.DueIn);
    }

    [Fact]
    public void TickBeforeTheTimeIsUpKeepsTheToast()
    {
        _queue.Show(Plain("a"));
        _time.Advance(3 * Second);

        Assert.False(_queue.Tick());
        Assert.Equal("a", _queue.Showing?.Key);
        Assert.Equal(Second, _queue.DueIn);
    }

    [Fact]
    public void HoverHoldsTheTimerAndLeavingResumesWhereItStopped()
    {
        _queue.Show(Plain("a"));
        Pass(3 * Second);
        _queue.SetHovered(true);

        Assert.Null(_queue.DueIn);
        Pass(30 * Second);
        Assert.Equal("a", _queue.Showing?.Key);

        _queue.SetHovered(false);
        Assert.Equal(Second, _queue.DueIn);
        Pass(Second);
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void KeyboardFocusHoldsTheTimerToo()
    {
        _queue.Show(Plain("a"));
        _queue.SetFocused(true);
        Pass(30 * Second);
        Assert.Equal("a", _queue.Showing?.Key);

        _queue.SetHovered(true);
        _queue.SetFocused(false);
        Assert.Null(_queue.DueIn); // still hovered

        _queue.SetHovered(false);
        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
    }

    [Fact]
    public void WhileADialogOrFlyoutIsOpenTheToastStepsAsideAndItsClockHolds()
    {
        _queue.Show(Plain("a"));
        Pass(3 * Second);
        _queue.SetBlocked(true);

        Assert.Null(_queue.Showing);
        Assert.Null(_queue.DueIn);
        Pass(30 * Second);

        _queue.SetBlocked(false);
        Assert.Equal("a", _queue.Showing?.Key);
        Assert.Equal(Second, _queue.DueIn);
    }

    [Fact]
    public void ToastsRequestedWhileBlockedWaitAndShowOnceItCloses()
    {
        _queue.SetBlocked(true);
        _queue.Show(Plain("a"));

        Assert.Null(_queue.Showing);
        Assert.Equal(1, _queue.WaitingCount);

        _queue.SetBlocked(false);
        Assert.Equal("a", _queue.Showing?.Key);
        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
    }

    [Fact]
    public void DismissClosesTheToastAndTheNextOneFollowsWithAFreshTimer()
    {
        _queue.Show(Plain("a"));
        _queue.Show(Plain("b"));
        Pass(2 * Second);

        _queue.Dismiss();

        Assert.Equal("b", _queue.Showing?.Key);
        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
    }

    [Fact]
    public void HoverOnAClosedToastDoesNotFreezeTheNextOne()
    {
        _queue.Show(Plain("a"));
        _queue.Show(Plain("b"));
        _queue.SetHovered(true);
        _queue.Dismiss();

        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
    }

    [Fact]
    public void ChangedIsRaisedWhenWhatIsShownChanges()
    {
        _queue.Show(Plain("a"));
        Assert.Equal(1, _changes);

        _queue.Show(Plain("b")); // waits: nothing visible changed
        Assert.Equal(1, _changes);

        _queue.Dismiss();
        Assert.Equal(2, _changes);
    }
}
