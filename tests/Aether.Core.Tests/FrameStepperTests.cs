using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class FrameStepperTests
{
    [Fact]
    public void Constructor_NullSender_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new FrameStepper(null!));
    }

    [Fact]
    public void Constructor_InvalidOptions_ThrowsArgumentException()
    {
        var sender = new FakeFrameStepCommandSender();
        var options = new FrameStepperOptions
        {
            MinInterval = TimeSpan.FromMilliseconds(200),
            StartInterval = TimeSpan.FromMilliseconds(100),
        };

        Assert.Throws<ArgumentException>(() => new FrameStepper(sender, options: options));
    }

    [Fact]
    public void Options_ZeroOrNegativeValues_ThrowArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { InitialRepeatDelay = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { InitialRepeatDelay = TimeSpan.FromMilliseconds(-10) });

        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { StartInterval = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { StartInterval = TimeSpan.FromMilliseconds(-1) });

        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { MinInterval = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { MinInterval = TimeSpan.FromMilliseconds(-1) });

        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { AccelerationTime = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { AccelerationTime = TimeSpan.FromSeconds(-2) });

        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { InFlightTimeout = TimeSpan.Zero });
        Assert.Throws<ArgumentOutOfRangeException>(() => new FrameStepperOptions { InFlightTimeout = TimeSpan.FromSeconds(-1) });
    }

    [Fact]
    public void Press_Forward_StepsImmediatelyInForwardDirectionWithTaggedId()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);

        Assert.True(stepper.IsHeld);
        Assert.Equal(FrameStepDirection.Forward, stepper.HeldDirection);
        Assert.True(stepper.IsStepInFlight);
        Assert.Equal(1, stepper.StepsSent);
        Assert.Equal(0, stepper.RepeatsDropped);
        Assert.Equal(1, sender.SendCount);

        RecordedStepCommand command = sender.LastCommand!.Value;
        Assert.Equal(FrameStepDirection.Forward, command.Direction);
        Assert.NotEqual(0UL, command.ReplyUserdata);
        Assert.Equal(IMpvCommandReplyHandler.FrameStepReplyTag, command.ReplyUserdata & 0xFFFF_0000_0000_0000);
    }

    [Fact]
    public void Press_Backward_StepsImmediatelyInBackwardDirectionWithTaggedId()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Backward);

        Assert.True(stepper.IsHeld);
        Assert.Equal(FrameStepDirection.Backward, stepper.HeldDirection);
        Assert.True(stepper.IsStepInFlight);
        Assert.Equal(1, stepper.StepsSent);
        Assert.Equal(1, sender.SendCount);

        RecordedStepCommand command = sender.LastCommand!.Value;
        Assert.Equal(FrameStepDirection.Backward, command.Direction);
        Assert.NotEqual(0UL, command.ReplyUserdata);
        Assert.Equal(IMpvCommandReplyHandler.FrameStepReplyTag, command.ReplyUserdata & 0xFFFF_0000_0000_0000);
    }

    [Fact]
    public void Press_SuccessiveSteps_GenerateUniqueIds()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);
        stepper.Release(FrameStepDirection.Forward);

        stepper.Press(FrameStepDirection.Forward);
        ulong id2 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id2, 0);

        Assert.NotEqual(id1, id2);
        Assert.Equal(IMpvCommandReplyHandler.FrameStepReplyTag, id1 & 0xFFFF_0000_0000_0000);
        Assert.Equal(IMpvCommandReplyHandler.FrameStepReplyTag, id2 & 0xFFFF_0000_0000_0000);
    }

    [Fact]
    public void Press_AlreadyHeldDirection_DoesNothing()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, stepper.StepsSent);

        stepper.Press(FrameStepDirection.Forward);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, stepper.StepsSent);
        Assert.Equal(0, stepper.RepeatsDropped);
    }

    [Fact]
    public void Press_OppositeDirection_SwitchesDirectionAndRestartsTiming()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);

        // Advance 200 ms (close to initial repeat delay of 250 ms)
        time.Advance(TimeSpan.FromMilliseconds(200));

        // Press opposite direction: should switch immediately
        stepper.Press(FrameStepDirection.Backward);
        Assert.Equal(FrameStepDirection.Backward, stepper.HeldDirection);
        Assert.Equal(2, sender.SendCount);
        Assert.Equal(FrameStepDirection.Backward, sender.LastCommand!.Value.Direction);

        ulong id2 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id2, 0);

        // Advance 200 ms from switch: timing was restarted to 250 ms, so no repeat yet
        time.Advance(TimeSpan.FromMilliseconds(200));
        TimeSpan? remaining = stepper.Tick();

        Assert.NotNull(remaining);
        Assert.Equal(TimeSpan.FromMilliseconds(50), remaining.Value);
        Assert.Equal(2, sender.SendCount); // no new step sent
    }

    [Fact]
    public void Release_NonHeldDirection_DoesNothing()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        Assert.True(stepper.IsHeld);

        stepper.Release(FrameStepDirection.Backward);

        Assert.True(stepper.IsHeld);
        Assert.Equal(FrameStepDirection.Forward, stepper.HeldDirection);
    }

    [Fact]
    public void Release_HeldDirection_StopsRepeatingAndTickReturnsNull()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        Assert.True(stepper.IsHeld);

        stepper.Release(FrameStepDirection.Forward);

        Assert.False(stepper.IsHeld);
        Assert.Null(stepper.HeldDirection);
        Assert.Null(stepper.Tick());
    }

    [Fact]
    public void Tick_WhenNotHeld_ReturnsNull()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        Assert.Null(stepper.Tick());
    }

    [Fact]
    public void Tick_BeforeInitialRepeatDelay_ReturnsRemainingDelayAndSendsNothing()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);

        time.Advance(TimeSpan.FromMilliseconds(100));
        TimeSpan? delay = stepper.Tick();

        // 250 ms - 100 ms = 150 ms remaining
        Assert.Equal(TimeSpan.FromMilliseconds(150), delay);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, stepper.StepsSent);
    }

    [Fact]
    public void Tick_AtInitialRepeatDelay_RepeatsIfPreviousReplyArrived()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);

        time.Advance(TimeSpan.FromMilliseconds(250));
        TimeSpan? delay = stepper.Tick();

        Assert.Equal(2, sender.SendCount);
        Assert.Equal(2, stepper.StepsSent);
        Assert.True(stepper.IsStepInFlight);
        // Next repeat interval starts at StartInterval (100 ms)
        Assert.Equal(TimeSpan.FromMilliseconds(100), delay);
    }

    [Fact]
    public void InFlight_RepeatDueWhileInFlight_IsDroppedAndNotQueued()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        // Do not reply to the first step: it stays in flight
        Assert.True(stepper.IsStepInFlight);

        time.Advance(TimeSpan.FromMilliseconds(250));
        TimeSpan? delay = stepper.Tick();

        Assert.Equal(1, stepper.RepeatsDropped);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, stepper.StepsSent);

        // Now reply arrives
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);
        Assert.False(stepper.IsStepInFlight);

        // Advance to next due tick: now the repeat can send
        time.Advance(delay!.Value);
        stepper.Tick();

        Assert.Equal(2, sender.SendCount);
        Assert.Equal(2, stepper.StepsSent);
        Assert.Equal(1, stepper.RepeatsDropped);
    }

    [Fact]
    public void FastTap_PressedWhileInFlight_IsQueuedAndSentAfterReply()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        stepper.Release(FrameStepDirection.Forward);
        stepper.Press(FrameStepDirection.Forward); // second tap lands on the step still in flight
        stepper.Release(FrameStepDirection.Forward);

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(1, stepper.TapsQueued);
        Assert.NotNull(stepper.Tick()); // keeps polling while the tap waits

        stepper.OnCommandReply(sender.LastCommand!.Value.ReplyUserdata, 0);
        time.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Null(stepper.Tick());

        Assert.Equal(2, sender.SendCount);
    }

    [Fact]
    public void FastTap_QueueHoldsOneTap_NewestWins()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        stepper.Release(FrameStepDirection.Forward);
        stepper.Press(FrameStepDirection.Forward);
        stepper.Release(FrameStepDirection.Forward);
        stepper.Press(FrameStepDirection.Backward);
        stepper.Release(FrameStepDirection.Backward);

        stepper.OnCommandReply(sender.LastCommand!.Value.ReplyUserdata, 0);
        stepper.Tick();

        Assert.Equal(2, sender.SendCount);
        Assert.Equal(FrameStepDirection.Backward, sender.LastCommand!.Value.Direction);
    }

    [Fact]
    public void ReleaseAll_DiscardsHeldKeyAndQueuedTap()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        stepper.Release(FrameStepDirection.Forward);
        stepper.Press(FrameStepDirection.Forward);
        stepper.ReleaseAll();

        Assert.False(stepper.IsHeld);
        stepper.OnCommandReply(sender.LastCommand!.Value.ReplyUserdata, 0);
        Assert.Null(stepper.Tick());
        Assert.Equal(1, sender.SendCount);
    }

    [Fact]
    public void OnCommandReply_NonMatchingTaggedId_DoesNotClearInFlight()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong actualId = sender.LastCommand!.Value.ReplyUserdata;

        ulong nonMatchingTaggedId = IMpvCommandReplyHandler.FrameStepReplyTag | 0x9999;
        bool handled = stepper.OnCommandReply(nonMatchingTaggedId, 0);

        Assert.True(handled);
        Assert.True(stepper.IsStepInFlight);

        // Replying with the actual id clears it
        stepper.OnCommandReply(actualId, 0);
        Assert.False(stepper.IsStepInFlight);
    }

    [Fact]
    public void OnCommandReply_UntaggedId_ReturnsFalseAndDoesNotClear()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);

        ulong untaggedId = 0x1234_0000_0000_0001;
        bool handled = stepper.OnCommandReply(untaggedId, 0);

        Assert.False(handled);
        Assert.True(stepper.IsStepInFlight);
    }

    [Fact]
    public void OnCommandReply_StaleReplyAfterTimeout_DoesNotClearNewerStep()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong step1Id = sender.LastCommand!.Value.ReplyUserdata;

        // Advance beyond in-flight timeout (1 second)
        time.Advance(TimeSpan.FromSeconds(1));
        stepper.Tick(); // Times out step1 and sends step2

        Assert.Equal(2, sender.SendCount);
        ulong step2Id = sender.LastCommand!.Value.ReplyUserdata;
        Assert.NotEqual(step1Id, step2Id);
        Assert.True(stepper.IsStepInFlight);

        // Stale reply for step1 arrives now
        bool handledStale = stepper.OnCommandReply(step1Id, 0);
        Assert.True(handledStale);
        Assert.True(stepper.IsStepInFlight); // step2 is still in flight!

        // Reply for step2 arrives
        bool handledStep2 = stepper.OnCommandReply(step2Id, 0);
        Assert.True(handledStep2);
        Assert.False(stepper.IsStepInFlight);
    }

    [Fact]
    public void InFlightTimeout_WithNoReply_NextStepGoesOutAfterTimeout()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        Assert.Equal(1, sender.SendCount);

        // Advance 500 ms (past 250 ms repeat delay, but before 1 s timeout)
        time.Advance(TimeSpan.FromMilliseconds(500));
        stepper.Tick();

        Assert.Equal(1, sender.SendCount); // Still blocked by in-flight
        Assert.Equal(1, stepper.RepeatsDropped);

        // Advance another 600 ms (total 1.1 s >= 1.0 s timeout)
        time.Advance(TimeSpan.FromMilliseconds(600));
        stepper.Tick();

        Assert.Equal(2, sender.SendCount); // Sent after timeout!
        Assert.Equal(2, stepper.StepsSent);
    }

    [Fact]
    public void OnCommandReply_NegativeError_IncrementsStepFailuresAndClearsInFlight()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id = sender.LastCommand!.Value.ReplyUserdata;

        bool handled = stepper.OnCommandReply(id, -1);

        Assert.True(handled);
        Assert.False(stepper.IsStepInFlight);
        Assert.Equal(1, stepper.StepFailures);
    }

    [Fact]
    public void OnCommandReply_NonNegativeError_ClearsInFlightWithoutIncrementingStepFailures()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.Press(FrameStepDirection.Forward);
        ulong id = sender.LastCommand!.Value.ReplyUserdata;

        bool handled = stepper.OnCommandReply(id, 0);

        Assert.True(handled);
        Assert.False(stepper.IsStepInFlight);
        Assert.Equal(0, stepper.StepFailures);
    }

    [Fact]
    public void SetBounds_AtStart_SuppressesBackwardButNotForward()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.SetBounds(atStart: true, atEnd: false);

        stepper.Press(FrameStepDirection.Backward);
        Assert.Equal(0, sender.SendCount);
        Assert.True(stepper.IsHeld);
        Assert.Equal(FrameStepDirection.Backward, stepper.HeldDirection);
        Assert.False(stepper.IsStepInFlight);

        stepper.Release(FrameStepDirection.Backward);
        Assert.False(stepper.IsHeld);

        stepper.Press(FrameStepDirection.Forward);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(FrameStepDirection.Forward, sender.LastCommand!.Value.Direction);
    }

    [Fact]
    public void SetBounds_AtEnd_SuppressesForwardButNotBackward()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.SetBounds(atStart: false, atEnd: true);

        stepper.Press(FrameStepDirection.Forward);
        Assert.Equal(0, sender.SendCount);
        Assert.True(stepper.IsHeld);
        Assert.Equal(FrameStepDirection.Forward, stepper.HeldDirection);
        Assert.False(stepper.IsStepInFlight);

        stepper.Release(FrameStepDirection.Forward);
        Assert.False(stepper.IsHeld);

        stepper.Press(FrameStepDirection.Backward);
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(FrameStepDirection.Backward, sender.LastCommand!.Value.Direction);
    }

    [Fact]
    public void SetBounds_HeldKeySuppressedAtStart_ResumesWhenBoundsClear()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        stepper.SetBounds(atStart: true, atEnd: false);
        stepper.Press(FrameStepDirection.Backward);
        Assert.Equal(0, sender.SendCount);
        Assert.True(stepper.IsHeld);

        // Advance time to when repeats would be due
        time.Advance(TimeSpan.FromMilliseconds(250));

        // Clear boundary
        stepper.SetBounds(atStart: false, atEnd: false);
        stepper.Tick();

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(FrameStepDirection.Backward, sender.LastCommand!.Value.Direction);
    }

    [Fact]
    public void Acceleration_SequenceOfDelaysFollowsCurve_ReachesMinInterval()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        // Auto-reply to every send
        sender.OnSend = (_, id) => stepper.OnCommandReply(id, 0);

        stepper.Press(FrameStepDirection.Forward);
        time.Advance(TimeSpan.FromMilliseconds(250));

        var delays = new List<TimeSpan>();
        TimeSpan? nextDelay = stepper.Tick();
        Assert.NotNull(nextDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(100), nextDelay.Value);
        delays.Add(nextDelay.Value);

        // Step through until at least 2.5 seconds total hold
        for (int i = 0; i < 40; i++)
        {
            time.Advance(nextDelay!.Value);
            nextDelay = stepper.Tick();
            Assert.NotNull(nextDelay);
            delays.Add(nextDelay.Value);
        }

        // Delay should never drop below MinInterval (33 ms)
        foreach (TimeSpan d in delays)
        {
            Assert.True(d >= TimeSpan.FromMilliseconds(33), $"Delay {d} was less than MinInterval 33 ms.");
        }

        // Last delays should reach exactly 33 ms
        Assert.Equal(TimeSpan.FromMilliseconds(33), delays[^1]);
    }

    [Fact]
    public void Acceleration_HoldOverThreeSeconds_TotalStepsWithinExpectedRange()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        sender.OnSend = (_, id) => stepper.OnCommandReply(id, 0);

        stepper.Press(FrameStepDirection.Forward);

        TimeSpan elapsed = TimeSpan.Zero;
        TimeSpan totalHold = TimeSpan.FromSeconds(3);

        while (elapsed < totalHold)
        {
            TimeSpan? nextDelay = stepper.Tick();
            if (nextDelay is null)
            {
                break;
            }

            time.Advance(nextDelay.Value);
            elapsed += nextDelay.Value;
        }

        // At t=0 (press = 1), 250ms delay, ~30 steps during 2s acceleration, ~22 steps during remaining 750ms -> ~53 steps
        Assert.InRange(stepper.StepsSent, 45, 60);
    }

    [Fact]
    public void UIStall_LongerThanInterval_DoesNotBurst_SendsOnlyOneStepPerTick()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        sender.OnSend = (_, id) => stepper.OnCommandReply(id, 0);

        stepper.Press(FrameStepDirection.Forward);
        time.Advance(TimeSpan.FromMilliseconds(250));
        stepper.Tick();

        int sentBeforeStall = sender.SendCount;

        // UI stalls for 500 ms (well beyond the 100 ms interval)
        time.Advance(TimeSpan.FromMilliseconds(500));
        TimeSpan? nextDelay = stepper.Tick();

        int sentAfterStall = sender.SendCount;

        Assert.Equal(sentBeforeStall + 1, sentAfterStall); // Exactly one step sent
        Assert.NotNull(nextDelay);
        Assert.True(nextDelay.Value > TimeSpan.Zero);
    }

    [Fact]
    public void SendFailed_SenderThrowsMpvException_RaisesEventClearsInFlightAndReleasesHeld()
    {
        var sender = new FakeFrameStepCommandSender
        {
            ExceptionToThrow = new MpvException(-1, "mpv_command_async"),
        };
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        int eventFiredCount = 0;
        Exception? receivedException = null;
        stepper.SendFailed += (_, ex) =>
        {
            eventFiredCount++;
            receivedException = ex;
        };

        stepper.Press(FrameStepDirection.Forward);

        Assert.Equal(1, eventFiredCount);
        Assert.NotNull(receivedException);
        Assert.IsType<MpvException>(receivedException);
        Assert.False(stepper.IsStepInFlight);
        Assert.False(stepper.IsHeld);
        Assert.Null(stepper.HeldDirection);
        Assert.Null(stepper.Tick());

        // Subsequent ticks return null and do not re-fire the event
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(stepper.Tick());
        Assert.Equal(1, eventFiredCount);
    }

    [Fact]
    public void SendFailed_SenderThrowsOnRepeatTick_ReleasesHeldAndTickReturnsNull()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        int eventFiredCount = 0;
        stepper.SendFailed += (_, _) => eventFiredCount++;

        // First press succeeds
        stepper.Press(FrameStepDirection.Forward);
        ulong id1 = sender.LastCommand!.Value.ReplyUserdata;
        stepper.OnCommandReply(id1, 0);

        // Now sender fails on repeat
        sender.ExceptionToThrow = new MpvException(-1, "mpv_command_async");
        time.Advance(TimeSpan.FromMilliseconds(250));

        TimeSpan? nextDelay = stepper.Tick();

        Assert.Null(nextDelay);
        Assert.Equal(1, eventFiredCount);
        Assert.False(stepper.IsHeld);
        Assert.False(stepper.IsStepInFlight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(42)]
    [InlineData(-2)]
    public void PressAndRelease_InvalidDirection_ThrowsArgumentOutOfRangeException(int invalidDir)
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        var direction = (FrameStepDirection)invalidDir;

        Assert.Throws<ArgumentOutOfRangeException>(() => stepper.Press(direction));
        Assert.Throws<ArgumentOutOfRangeException>(() => stepper.Release(direction));
    }

    [Fact]
    public void Threading_OnCommandReplyFromBackgroundThread_ClearsInFlightDeterministically()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        for (int i = 0; i < 20; i++)
        {
            stepper.Press(FrameStepDirection.Forward);
            Assert.True(stepper.IsStepInFlight);

            ulong currentId = sender.LastCommand!.Value.ReplyUserdata;

            // Reply asynchronously from a background thread
            Task.Run(() =>
            {
                Thread.Sleep(5);
                stepper.OnCommandReply(currentId, 0);
            });

            bool cleared = TestWait.ForCondition(() => !stepper.IsStepInFlight, timeoutMs: 2000);
            Assert.True(cleared, $"In-flight was not cleared on iteration {i}.");

            stepper.Release(FrameStepDirection.Forward);
        }
    }

    [Fact]
    public void Threading_ConcurrentTicksAndBackgroundReplies_AdvancesStepsWithoutLostInFlightClear()
    {
        var sender = new FakeFrameStepCommandSender();
        var time = new ManualTimeProvider();
        var stepper = new FrameStepper(sender, time);

        // Background threadpool reply upon each send
        sender.OnSend = (_, replyId) =>
        {
            Task.Run(() =>
            {
                Thread.Sleep(2);
                stepper.OnCommandReply(replyId, 0);
            });
        };

        stepper.Press(FrameStepDirection.Forward);

        for (int i = 0; i < 30; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(100));
            stepper.Tick();
            Thread.Sleep(5);
        }

        bool cleared = TestWait.ForCondition(() => !stepper.IsStepInFlight, timeoutMs: 2000);
        Assert.True(cleared, "Final in-flight step was not cleared by background thread reply.");
        Assert.True(stepper.StepsSent > 1, $"Expected multiple steps to be sent, but only sent {stepper.StepsSent}.");

        stepper.Release(FrameStepDirection.Forward);
    }
}
