using System;
using System.Collections.Generic;
using System.Threading;
using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public unsafe class PlayerEventDispatcherTests
{
    private sealed class FakeCommandReplyHandler : IMpvCommandReplyHandler
    {
        public readonly List<(ulong ReplyUserdata, int Error)> Replies = new();

        public bool OnCommandReply(ulong replyUserdata, int error)
        {
            Replies.Add((replyUserdata, error));
            return true;
        }
    }

    [Fact]
    public void Property_Position_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 42.5);
        dispatcher.OnEvent(new MpvEventView(evt));

        Assert.Equal(1, scheduler.RequestFlushCount);
        Assert.Equal(0, vm.Position); // Not applied until Flush

        dispatcher.Flush();
        Assert.Equal(42.5, vm.Position);
    }

    [Fact]
    public void Property_Duration_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Duration, 120.0);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.Equal(120.0, vm.Duration);
    }

    [Fact]
    public void Property_Speed_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Speed, 2.5);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.Equal(2.5, vm.Speed);
    }

    [Fact]
    public void Property_Volume_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Volume, 75.0);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.Equal(75.0, vm.Volume);
    }

    [Fact]
    public void Property_ContainerFps_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.ContainerFps, 59.94);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.Equal(59.94, vm.FramesPerSecond);
    }

    [Fact]
    public void Property_Pause_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyFlag((ulong)PlayerProperty.Pause, true);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.True(vm.IsPaused);
    }

    [Fact]
    public void Property_EofReached_UpdatesFieldAfterFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyFlag((ulong)PlayerProperty.EofReached, true);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.True(vm.IsAtEnd);
    }

    [Fact]
    public void Property_IdleActive_UpdatesIdleAndDerivesPlaying()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyFlag((ulong)PlayerProperty.IdleActive, false);
        dispatcher.OnEvent(new MpvEventView(evt));
        dispatcher.Flush();

        Assert.Equal(PlaybackStatus.Playing, vm.Status);
    }

    [Fact]
    public void Property_Unavailable_ResetsToDefaults()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        // First set non-default values on all properties
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.Position, 33.3)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.Duration, 100.0)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.Speed, 1.75)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.Volume, 50.0)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyDouble((ulong)PlayerProperty.ContainerFps, 60.0)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.Pause, true)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.EofReached, true)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.IdleActive, false)));
        dispatcher.Flush();

        Assert.Equal(33.3, vm.Position);
        Assert.Equal(100.0, vm.Duration);
        Assert.Equal(1.75, vm.Speed);
        Assert.Equal(50.0, vm.Volume);
        Assert.Equal(60.0, vm.FramesPerSecond);
        Assert.True(vm.IsPaused);
        Assert.True(vm.IsAtEnd);

        // Now send unavailable for all properties
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.Position)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.Duration)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.Speed)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.Volume)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.ContainerFps)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.Pause)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.EofReached)));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyUnavailable((ulong)PlayerProperty.IdleActive)));
        dispatcher.Flush();

        Assert.Equal(0, vm.Position);
        Assert.Equal(0, vm.Duration);
        Assert.Equal(1, vm.Speed);
        Assert.Equal(100, vm.Volume);
        Assert.Equal(0, vm.FramesPerSecond);
        Assert.False(vm.IsPaused);
        Assert.False(vm.IsAtEnd);
        Assert.Equal(PlaybackStatus.Idle, vm.Status);
    }

    [Fact]
    public void UnknownPropertyId_IsIgnored_AndDoesNotRequestFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreatePropertyDouble(9999, 123.45);
        dispatcher.OnEvent(new MpvEventView(evt));

        Assert.Equal(0, scheduler.RequestFlushCount);
        dispatcher.Flush();

        Assert.Equal(PlayerState.Initial, vm.State);
    }

    [Fact]
    public void Coalescing_OneThousandEventsBeforeFlush_RequestsFlushExactlyOnce()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        for (int i = 1; i <= 1000; i++)
        {
            MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Position, i);
            dispatcher.OnEvent(new MpvEventView(evt));
        }

        Assert.Equal(1, scheduler.RequestFlushCount);
        Assert.Equal(0, vm.Position);

        dispatcher.Flush();
        Assert.Equal(1000, vm.Position);

        // Next change after flush requests again
        MpvEvent* nextEvt = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 1001);
        dispatcher.OnEvent(new MpvEventView(nextEvt));

        Assert.Equal(2, scheduler.RequestFlushCount);
        dispatcher.Flush();
        Assert.Equal(1001, vm.Position);

        // An event that leaves state unchanged does not request a flush
        MpvEvent* sameEvt = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 1001);
        dispatcher.OnEvent(new MpvEventView(sameEvt));

        Assert.Equal(2, scheduler.RequestFlushCount);
    }

    [Fact]
    public void StatusDerivation_LifecycleTransitions()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        // 1. Initial status is Idle
        Assert.Equal(PlaybackStatus.Idle, vm.Status);

        // 2. StartFile -> Loading
        dispatcher.OnEvent(new MpvEventView(events.CreateStartFile()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Loading, vm.Status);

        // 3. FileLoaded -> back to Idle because IdleActive is still true
        dispatcher.OnEvent(new MpvEventView(events.CreateFileLoaded()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Idle, vm.Status);

        // 4. IdleActive false + Pause false -> Playing
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.IdleActive, false)));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Playing, vm.Status);

        // 5. Pause true -> Paused
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.Pause, true)));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Paused, vm.Status);

        // 6. StartFile while paused -> Loading until FileLoaded -> Paused
        dispatcher.OnEvent(new MpvEventView(events.CreateStartFile()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Loading, vm.Status);

        dispatcher.OnEvent(new MpvEventView(events.CreateFileLoaded()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Paused, vm.Status);

        // 7. Unpause -> Playing
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.Pause, false)));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Playing, vm.Status);

        // 8. EofReached true -> Ended
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.EofReached, true)));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Ended, vm.Status);

        // 9. EndFile -> Idle
        dispatcher.OnEvent(new MpvEventView(events.CreateEndFile()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Idle, vm.Status);

        // 10. Start playing again, then Shutdown -> Idle
        dispatcher.OnEvent(new MpvEventView(events.CreateStartFile()));
        dispatcher.OnEvent(new MpvEventView(events.CreateFileLoaded()));
        dispatcher.OnEvent(new MpvEventView(events.CreatePropertyFlag((ulong)PlayerProperty.EofReached, false)));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Playing, vm.Status);

        dispatcher.OnEvent(new MpvEventView(events.CreateShutdown()));
        dispatcher.Flush();
        Assert.Equal(PlaybackStatus.Idle, vm.Status);
    }

    [Fact]
    public void CommandReply_ForwardedToHandler()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        var handler = new FakeCommandReplyHandler();
        dispatcher.CommandReplies = handler;

        using var events = new NativeMpvEvents();
        MpvEvent* evt = events.CreateCommandReply(0x5354_0000_0000_0001UL, -4);
        dispatcher.OnEvent(new MpvEventView(evt));

        Assert.Single(handler.Replies);
        Assert.Equal((0x5354_0000_0000_0001UL, -4), handler.Replies[0]);
        Assert.Equal(0, scheduler.RequestFlushCount);
    }

    [Fact]
    public void CommandReply_WhenNoHandlerSet_DoesNotCrash()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);

        using var events = new NativeMpvEvents();
        MpvEvent* evt = events.CreateCommandReply(0x1234UL, 0);
        dispatcher.OnEvent(new MpvEventView(evt));

        Assert.Null(dispatcher.CommandReplies);
    }

    [Fact]
    public void OnFault_RaisedDuringFlushOnce_WithLatestException()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);

        var faults = new List<Exception>();
        dispatcher.Faulted += (_, ex) => faults.Add(ex);

        var firstEx = new InvalidOperationException("First error");
        var secondEx = new InvalidOperationException("Second error");

        dispatcher.OnFault(firstEx);

        Assert.Empty(faults); // Not raised synchronously
        Assert.Equal(1, scheduler.RequestFlushCount);

        dispatcher.OnFault(secondEx);
        Assert.Empty(faults);
        Assert.Equal(1, scheduler.RequestFlushCount); // Coalesced

        dispatcher.Flush();

        Assert.Single(faults);
        Assert.Same(secondEx, faults[0]);

        // Second flush does not re-raise
        dispatcher.Flush();
        Assert.Single(faults);
    }

    [Fact]
    public void SchedulerThrows_OnEventRethrows_AndNextEventRequestsFlushAgain()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler { ShouldThrow = true };
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt1 = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 5.0);
        Assert.Throws<InvalidOperationException>(() => dispatcher.OnEvent(new MpvEventView(evt1)));
        Assert.Equal(1, scheduler.RequestFlushCount);

        // Next event should try RequestFlush again since dirty was reset on exception
        scheduler.ShouldThrow = false;
        MpvEvent* evt2 = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 6.0);
        dispatcher.OnEvent(new MpvEventView(evt2));

        Assert.Equal(2, scheduler.RequestFlushCount);
        dispatcher.Flush();
        Assert.Equal(6.0, vm.Position);
    }

    [Fact]
    public void QueueOverflow_RequestsFlush()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);
        using var events = new NativeMpvEvents();

        MpvEvent* evt = events.CreateQueueOverflow();
        dispatcher.OnEvent(new MpvEventView(evt));

        Assert.Equal(1, scheduler.RequestFlushCount);

        // Coalesced if already dirty
        dispatcher.OnEvent(new MpvEventView(evt));
        Assert.Equal(1, scheduler.RequestFlushCount);

        dispatcher.Flush();

        // Requests again after flush
        dispatcher.OnEvent(new MpvEventView(evt));
        Assert.Equal(2, scheduler.RequestFlushCount);
    }

    [Fact]
    public void ZeroAllocation_OnEvent_AcrossOneThousandCalls()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);

        using var events = new NativeMpvEvents();
        const int count = 1000;
        var eventPointers = new IntPtr[count];
        for (int i = 0; i < count; i++)
        {
            eventPointers[i] = (IntPtr)events.CreatePropertyDouble((ulong)PlayerProperty.Position, i + 1);
        }

        // Warm up JIT and execution paths
        for (int i = 0; i < 50; i++)
        {
            dispatcher.OnEvent(new MpvEventView((MpvEvent*)eventPointers[i]));
            dispatcher.Flush();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < count; i++)
        {
            dispatcher.OnEvent(new MpvEventView((MpvEvent*)eventPointers[i]));
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ZeroAllocation_FlushAndApply_WithNoSubscribers()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);

        using var events = new NativeMpvEvents();
        MpvEvent* evtA = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 10.0);
        MpvEvent* evtB = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 20.0);

        // Warm up JIT
        for (int i = 0; i < 50; i++)
        {
            dispatcher.OnEvent(new MpvEventView((i & 1) == 0 ? evtA : evtB));
            dispatcher.Flush();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long before = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 1000; i++)
        {
            dispatcher.OnEvent(new MpvEventView((i & 1) == 0 ? evtA : evtB));
            dispatcher.Flush();
        }

        long after = GC.GetAllocatedBytesForCurrentThread();
        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ThreadSafety_ConcurrentOnEventAndFlush_NeverLosesFinalValue()
    {
        var vm = new PlayerViewModel();
        var scheduler = new FakeUiFlushScheduler();
        var dispatcher = new PlayerEventDispatcher(vm, scheduler);

        const int iterations = 10_000;
        using var events = new NativeMpvEvents();
        MpvEvent* evt = events.CreatePropertyDouble((ulong)PlayerProperty.Position, 0);

        var readyEvent = new ManualResetEventSlim(false);
        var startEvent = new ManualResetEventSlim(false);

        var backgroundThread = new Thread(() =>
        {
            readyEvent.Set();
            startEvent.Wait();

            for (int i = 1; i <= iterations; i++)
            {
                NativeMpvEvents.UpdatePropertyDouble(evt, i);
                dispatcher.OnEvent(new MpvEventView(evt));
            }
        })
        {
            IsBackground = true,
            Name = "Test.OnEventThread"
        };

        backgroundThread.Start();

        bool ready = readyEvent.Wait(TimeSpan.FromSeconds(5));
        Assert.True(ready, "Background thread did not become ready.");
        startEvent.Set();

        while (backgroundThread.IsAlive)
        {
            dispatcher.Flush();
            Thread.Yield();
        }

        bool joined = backgroundThread.Join(TimeSpan.FromSeconds(5));
        Assert.True(joined, "Background thread did not complete within timeout.");

        // One final flush to apply whatever state was set last
        dispatcher.Flush();

        Assert.Equal(iterations, vm.Position);
    }
}
