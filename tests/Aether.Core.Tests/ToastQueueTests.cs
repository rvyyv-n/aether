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

    [Fact]
    public void EmptyQueueDismissAndTickAreSafeNoOps()
    {
        Assert.Null(_queue.Showing);
        Assert.Null(_queue.DueIn);
        Assert.Equal(0, _queue.WaitingCount);

        _queue.Dismiss();
        Assert.False(_queue.Tick());
        Assert.Null(_queue.Showing);
        Assert.Equal(0, _changes);
    }

    [Fact]
    public void ManyDuplicatesWhileShowingKeepRestartingTimer()
    {
        _queue.Show(Plain("item"));

        // Each duplicate resets the timer back to 4s without adding waiting entries
        for (int i = 0; i < 5; i++)
        {
            Pass(2 * Second);
            _queue.Show(Plain("item"));
            Assert.Equal(0, _queue.WaitingCount);
            Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);
        }

        Pass(ToastQueue.DefaultDuration);
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void ManyDuplicatesWhileWaitingCollapseInPlacePreservingQueueOrder()
    {
        _queue.Show(Plain("active"));
        _queue.Show(new ToastRequest("queued", "v1"));
        _queue.Show(Plain("other"));

        // Multiple duplicates collapse into the existing waiting slot, keeping queue position
        for (int i = 2; i <= 5; i++)
        {
            _queue.Show(new ToastRequest("queued", $"v{i}"));
        }

        Assert.Equal(2, _queue.WaitingCount);

        Pass(ToastQueue.DefaultDuration);
        Assert.Equal("queued", _queue.Showing?.Key);
        Assert.Equal("v5", _queue.Showing?.Text);

        Pass(ToastQueue.DefaultDuration);
        Assert.Equal("other", _queue.Showing?.Key);
    }

    [Fact]
    public void DismissWhileQueuedAdvancesThroughQueueUntilEmpty()
    {
        _queue.Show(Plain("a"));
        _queue.Show(Plain("b"));
        _queue.Show(Plain("c"));

        _queue.Dismiss();
        Assert.Equal("b", _queue.Showing?.Key);
        Assert.Equal(1, _queue.WaitingCount);

        _queue.Dismiss();
        Assert.Equal("c", _queue.Showing?.Key);
        Assert.Equal(0, _queue.WaitingCount);

        _queue.Dismiss();
        Assert.Null(_queue.Showing);
        Assert.Equal(0, _queue.WaitingCount);

        // Calling dismiss once more on the now-empty queue is safe
        _queue.Dismiss();
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void DismissWhileBlockedIsIgnored()
    {
        _queue.Show(Plain("a"));
        Pass(1 * Second);

        _queue.SetBlocked(true);
        _queue.Dismiss();

        _queue.SetBlocked(false);
        Assert.Equal("a", _queue.Showing?.Key);
        Assert.Equal(3 * Second, _queue.DueIn);
    }

    [Fact]
    public void FocusPausesTimerAndResumesWithRemainingTime()
    {
        _queue.Show(Plain("a"));
        Pass(2500 * TimeSpan.FromMilliseconds(1));

        _queue.SetFocused(true);
        Assert.Null(_queue.DueIn);
        Pass(10 * Second);

        _queue.SetFocused(false);
        Assert.Equal(1500 * TimeSpan.FromMilliseconds(1), _queue.DueIn);

        Pass(1500 * TimeSpan.FromMilliseconds(1));
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void SimultaneousHoverAndFocusHoldTimerUntilBothAreReleased()
    {
        _queue.Show(Plain("a"));
        Pass(1 * Second); // 3s remaining

        _queue.SetHovered(true);
        _queue.SetFocused(true);
        Pass(5 * Second);

        // Releasing focus while still hovered keeps the timer paused
        _queue.SetFocused(false);
        Assert.Null(_queue.DueIn);
        Pass(5 * Second);

        // Releasing hover resumes from the 3s that were remaining
        _queue.SetHovered(false);
        Assert.Equal(3 * Second, _queue.DueIn);

        Pass(3 * Second);
        Assert.Null(_queue.Showing);
    }

    [Fact]
    public void FailuresQueuedWhileBlockedJumpAheadOfWaitingNeutrals()
    {
        _queue.SetBlocked(true);
        _queue.Show(Plain("n1"));
        _queue.Show(Failure("f1"));
        _queue.Show(Plain("n2"));
        _queue.Show(Failure("f2"));

        Assert.Null(_queue.Showing);
        Assert.Equal(4, _queue.WaitingCount);

        _queue.SetBlocked(false);

        // Failures show first in insertion order, then neutrals in order
        var order = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            order.Add(_queue.Showing!.Key);
            Pass(ToastQueue.DefaultDuration);
        }

        Assert.Equal(["f1", "f2", "n1", "n2"], order);
    }

    [Fact]
    public void SuspendedToastResumesBeforeQueuedFailuresWhenUnblocked()
    {
        _queue.Show(Plain("active"));
        Pass(1 * Second);

        _queue.SetBlocked(true);
        _queue.Show(Failure("fail"));

        // While blocked, nothing is showing
        Assert.Null(_queue.Showing);

        // When unblocked, the previously active toast finishes its remaining time before the failure
        _queue.SetBlocked(false);
        Assert.Equal("active", _queue.Showing?.Key);
        Assert.Equal(3 * Second, _queue.DueIn);

        Pass(3 * Second);
        Assert.Equal("fail", _queue.Showing?.Key);
    }

    [Fact]
    public void DuplicateWithActionPromotesDurationToEightSeconds()
    {
        _queue.Show(Plain("key"));
        Assert.Equal(ToastQueue.DefaultDuration, _queue.DueIn);

        // A duplicate with an action extends the timer to 8s
        _queue.Show(WithAction("key"));
        Assert.Equal(ToastQueue.DefaultActionDuration, _queue.DueIn);
    }

    [Fact]
    public void SuccessToneIsQueuedInOrderBehindFailures()
    {
        _queue.Show(Plain("active"));
        _queue.Show(new ToastRequest("success", "Saved", ToastTone.Success));
        _queue.Show(Failure("fail"));
        _queue.Show(Plain("plain"));

        var order = new List<string>();
        for (int i = 0; i < 4; i++)
        {
            order.Add(_queue.Showing!.Key);
            Pass(ToastQueue.DefaultDuration);
        }

        Assert.Equal(["active", "fail", "success", "plain"], order);
    }
}
