using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class SeekCoalescerSkipTests
{
    [Fact]
    public void SeekBy_WhenIdle_SendsTheSkipAtOnce()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender, new ManualTimeProvider());

        coalescer.SeekBy(5);

        Assert.Equal([5.0], sender.SentSkips.Select(s => s.Delta));
        Assert.True(coalescer.IsSeekInFlight);
        Assert.False(coalescer.HasPendingSeek);
    }

    [Fact]
    public void SeekBy_WhileOneIsInFlight_AddsTheSkipsUpAndSendsOneOnReply()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender, new ManualTimeProvider());

        coalescer.SeekBy(5);
        ulong firstReply = sender.SentSkips[0].ReplyUserdata;
        coalescer.SeekBy(5);
        coalescer.SeekBy(5);
        coalescer.SeekBy(-1);

        Assert.Single(sender.SentSkips);
        Assert.Equal(9, coalescer.PendingSkip);
        Assert.Equal(3, coalescer.SeeksCoalesced);

        coalescer.OnCommandReply(firstReply, error: 0);

        Assert.Equal([5.0, 9.0], sender.SentSkips.Select(s => s.Delta));
        Assert.Null(coalescer.PendingSkip);
    }

    [Fact]
    public void SeekBy_AfterAQueuedSeekBarTarget_MovesThatTarget()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender, new ManualTimeProvider());

        coalescer.Seek(10, dragging: false);
        coalescer.Seek(20, dragging: false);
        coalescer.SeekBy(5);

        Assert.Equal(25, coalescer.PendingPosition);
        Assert.Null(coalescer.PendingSkip);
    }

    [Fact]
    public void Seek_AfterAQueuedSkip_ReplacesIt()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender, new ManualTimeProvider());

        coalescer.SeekBy(5);
        coalescer.SeekBy(5);
        coalescer.Seek(30, dragging: false);

        Assert.Null(coalescer.PendingSkip);
        Assert.Equal(30, coalescer.PendingPosition);
    }

    [Fact]
    public void Reset_DropsAQueuedSkip()
    {
        var sender = new FakeSeekCommandSender();
        var coalescer = new SeekCoalescer(sender, new ManualTimeProvider());

        coalescer.SeekBy(5);
        coalescer.SeekBy(5);
        coalescer.Reset();

        Assert.False(coalescer.HasPendingSeek);
    }
}
