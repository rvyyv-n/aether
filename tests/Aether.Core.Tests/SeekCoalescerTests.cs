using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class SeekCoalescerTests
{
    [Fact]
    public void Constructor_NullSender_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new SeekCoalescer(null!));
    }

    [Fact]
    public void Options_InvalidTimeout_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeekCoalescerOptions { InFlightTimeout = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SeekCoalescerOptions { InFlightTimeout = TimeSpan.FromMilliseconds(-5) }.Validate());
    }

    [Fact]
    public void Seek_WhenIdle_DispatchesImmediatelyWithTaggedId()
    {
        var sender = new FakeSeekCommandSender();
        var time = new ManualTimeProvider();
        var coalescer = new SeekCoalescer(sender, time);

        coalescer.Seek(12.5, dragging: true);

        Assert.Equal(1, sender.SendCount);
        Assert.Equal(12.5, sender.LastCommand!.Value.Seconds);
        Assert.True(sender.LastCommand!.Value.Dragging);
        Assert.Equal(IMpvCommandReplyHandler.SeekReplyTag, sender.LastCommand!.Value.ReplyUserdata & 0xFFFF_0000_0000_0000);
        Assert.True(coalescer.IsSeekInFlight);
        Assert.False(coalescer.HasPendingSeek);
        Assert.Equal(1, coalescer.SeeksSent);
        Assert.Equal(0, coalescer.SeeksCoalesced);
    }

    [Fact]
    public void Seek_DuringDrag_CoalescesRapidEventsAndDispatchesLatestOnReply()
    {
        var sender = new FakeSeekCommandSender();
        var time = new ManualTimeProvider();
        var coalescer = new SeekCoalescer(sender, time);

        // First seek goes in flight.
        coalescer.Seek(10.0, dragging: true);
        ulong firstReplyId = sender.LastCommand!.Value.ReplyUserdata;
        Assert.Equal(1, sender.SendCount);

        // Rapid mouse movements (e.g. 50 drag events).
        for (int i = 1; i <= 50; i++)
        {
            time.Advance(TimeSpan.FromMilliseconds(1));
            coalescer.Seek(10.0 + i * 0.1, dragging: true);
        }

        // None of the 50 intermediate seeks should have been sent to mpv yet.
        Assert.Equal(1, sender.SendCount);
        Assert.Equal(50, coalescer.SeeksCoalesced);
        Assert.True(coalescer.HasPendingSeek);
        Assert.Equal(15.0, coalescer.PendingPosition);
        Assert.True(coalescer.PendingDragging);

        // Event thread: reply arrives for first seek.
        bool handled = coalescer.OnCommandReply(firstReplyId, error: 0);

        Assert.True(handled);
        Assert.Equal(2, sender.SendCount);
        Assert.Equal(15.0, sender.LastCommand!.Value.Seconds);
        Assert.True(sender.LastCommand!.Value.Dragging);
        Assert.False(coalescer.HasPendingSeek);
        Assert.True(coalescer.IsSeekInFlight);
        Assert.Equal(2, coalescer.SeeksSent);
    }

    [Fact]
    public void Seek_ReleaseAfterDrags_PreservesExactSeek()
    {
        var sender = new FakeSeekCommandSender();
        var time = new ManualTimeProvider();
        var coalescer = new SeekCoalescer(sender, time);

        // Initial drag seek.
        coalescer.Seek(5.0, dragging: true);
        ulong replyId = sender.LastCommand!.Value.ReplyUserdata;

        // More drag moves.
        coalescer.Seek(6.0, dragging: true);
        coalescer.Seek(7.0, dragging: true);

        // User releases thumb: exact seek requested.
        coalescer.Seek(7.5, dragging: false);

        Assert.Equal(1, sender.SendCount);
        Assert.True(coalescer.HasPendingSeek);
        Assert.Equal(7.5, coalescer.PendingPosition);
        Assert.False(coalescer.PendingDragging); // exact, not dragging!

        // Reply arrives.
        coalescer.OnCommandReply(replyId, error: 0);

        Assert.Equal(2, sender.SendCount);
        Assert.Equal(7.5, sender.LastCommand!.Value.Seconds);
        Assert.False(sender.LastCommand!.Value.Dragging); // Must land on exact frame!
    }

    [Fact]
    public void Seek_ExtraDragAfterRelease_DoesNotDowngradeReleaseToKeyframe()
    {
        var sender = new FakeSeekCommandSender();
        var time = new ManualTimeProvider();
        var coalescer = new SeekCoalescer(sender, time);

        coalescer.Seek(1.0, dragging: true);
        ulong replyId = sender.LastCommand!.Value.ReplyUserdata;

        // Release seek queued.
        coalescer.Seek(2.0, dragging: false);

        // Spurious trailing drag event from UI event queue.
        coalescer.Seek(2.1, dragging: true);

        // Must still preserve dragging = false so it lands exact.
        Assert.False(coalescer.PendingDragging);
        Assert.Equal(2.1, coalescer.PendingPosition);

        coalescer.OnCommandReply(replyId, error: 0);
        Assert.False(sender.LastCommand!.Value.Dragging);
    }

    [Fact]
    public void OnCommandReply_UnmatchedTag_ReturnsFalse()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender);

        ulong frameStepReply = IMpvCommandReplyHandler.FrameStepReplyTag | 0x01;
        bool handled = coalescer.OnCommandReply(frameStepReply, error: 0);

        Assert.False(handled);
        Assert.Equal(0, coalescer.RepliesHandled);
    }

    [Fact]
    public void InFlightTimeout_AllowsSubsequentSeekAfterTimeout()
    {
        var sender = new FakeSeekCommandSender();
        var time = new ManualTimeProvider();
        var options = new SeekCoalescerOptions { InFlightTimeout = TimeSpan.FromMilliseconds(100) };
        var coalescer = new SeekCoalescer(sender, time, options);

        coalescer.Seek(10.0, dragging: true);
        Assert.Equal(1, sender.SendCount);

        // Advance past timeout without reply.
        time.Advance(TimeSpan.FromMilliseconds(150));

        // Next seek should detect timeout and dispatch immediately.
        coalescer.Seek(20.0, dragging: true);
        Assert.Equal(2, sender.SendCount);
        Assert.Equal(20.0, sender.LastCommand!.Value.Seconds);
    }

    [Fact]
    public void Reset_ClearsInFlightAndPendingState()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender);

        coalescer.Seek(10.0, dragging: true);
        coalescer.Seek(15.0, dragging: true);

        Assert.True(coalescer.IsSeekInFlight);
        Assert.True(coalescer.HasPendingSeek);

        coalescer.Reset();

        Assert.False(coalescer.IsSeekInFlight);
        Assert.False(coalescer.HasPendingSeek);
        Assert.Null(coalescer.PendingPosition);
    }

    [Fact]
    public void SendFailed_RaisedWhenSenderRejectsSeek()
    {
        var sender = new FakeSeekCommandSender
        {
            ExceptionToThrow = new MpvException(-1, "mpv shutting down")
        };
        var coalescer = new SeekCoalescer(sender);

        Exception? caught = null;
        coalescer.SendFailed += (_, ex) => caught = ex;

        coalescer.Seek(10.0, dragging: true);

        Assert.NotNull(caught);
        Assert.False(coalescer.IsSeekInFlight);
    }

    [Fact]
    public void CompositeCommandReplyHandler_FansOutCorrectly()
    {
        var stepSender = new FakeFrameStepCommandSender();
        var stepper = new FrameStepper(stepSender);

        var seekSender = new FakeSeekCommandSender();
        var seekCoalescer = new SeekCoalescer(seekSender);

        var composite = new CompositeCommandReplyHandler(stepper, seekCoalescer);

        // Frame step reply.
        ulong stepReplyId = IMpvCommandReplyHandler.FrameStepReplyTag | 0x01;
        Assert.True(composite.OnCommandReply(stepReplyId, 0));

        // Seek reply.
        ulong seekReplyId = IMpvCommandReplyHandler.SeekReplyTag | 0x01;
        Assert.True(composite.OnCommandReply(seekReplyId, 0));

        // Unknown tag.
        ulong unknownReplyId = 0x9999_0000_0000_0001;
        Assert.False(composite.OnCommandReply(unknownReplyId, 0));
    }

    [Fact]
    public async Task ConcurrentHammer_ThreadSafetyStressTest()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        var token = cts.Token;

        // Background thread simulating event replies.
        var replyTask = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (sender.LastCommand is { } cmd)
                {
                    coalescer.OnCommandReply(cmd.ReplyUserdata, error: 0);
                }
                Thread.Yield();
            }
        });

        // UI thread issuing rapid burst seeks.
        double pos = 0;
        while (!token.IsCancellationRequested)
        {
            pos += 0.05;
            coalescer.Seek(pos, dragging: true);
        }

        await replyTask;

        Assert.True(coalescer.SeeksSent > 0);
        Assert.True(coalescer.SeeksCoalesced > 0);
    }
}
