using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aether.Core.Interop.Mpv;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class MpvEventLoopTests
{
    [Fact]
    public void Constructor_NullSource_ThrowsArgumentNullException()
    {
        var sink = new TestEventSink();
        Assert.Throws<ArgumentNullException>(() => new MpvEventLoop((IMpvEventSource)null!, sink));
    }

    [Fact]
    public void Constructor_NullSink_ThrowsArgumentNullException()
    {
        using var source = new FakeMpvEventSource();
        Assert.Throws<ArgumentNullException>(() => new MpvEventLoop(source, null!));
    }

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        var sink = new TestEventSink();
        Assert.Throws<ArgumentNullException>(() => new MpvEventLoop((MpvContext)null!, sink));
    }

    [Fact]
    public unsafe void MpvEventView_Constructor_NullPointer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MpvEventView(null));
    }

    [Fact]
    public void Events_ReachSinkInOrder_WithCorrectId_Error_ReplyUserdata()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        source.Enqueue(MpvEventId.FileLoaded, error: 0, replyUserdata: 101);
        source.Enqueue(MpvEventId.Seek, error: -1, replyUserdata: 202);
        source.Enqueue(MpvEventId.CommandReply, error: 5, replyUserdata: 303);

        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 3));
        Assert.True(loop.Stop(2000));

        Assert.Equal(3, sink.Count);
        Assert.Equal(3, loop.EventsDispatched);

        Assert.Equal(MpvEventId.FileLoaded, sink.Events[0].Id);
        Assert.Equal(0, sink.Events[0].Error);
        Assert.Equal(101UL, sink.Events[0].ReplyUserdata);

        Assert.Equal(MpvEventId.Seek, sink.Events[1].Id);
        Assert.Equal(-1, sink.Events[1].Error);
        Assert.Equal(202UL, sink.Events[1].ReplyUserdata);

        Assert.Equal(MpvEventId.CommandReply, sink.Events[2].Id);
        Assert.Equal(5, sink.Events[2].Error);
        Assert.Equal(303UL, sink.Events[2].ReplyUserdata);
    }

    [Fact]
    public void PropertyPayloads_DecodeCorrectly_ForDouble_Int64_Flag_String_AndUnavailable()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        source.EnqueuePropertyDouble(1, "time-pos", 123.456);
        source.EnqueuePropertyInt64(2, "playlist-pos", 9999999999L);
        source.EnqueuePropertyFlag(3, "pause", true);
        source.EnqueuePropertyFlag(4, "mute", false);
        source.EnqueuePropertyString(5, "media-title", "Aether 日本語 Test 🎬");
        source.EnqueuePropertyUnavailable(6, "demuxer-cache-state");

        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 6));
        Assert.True(loop.Stop(2000));

        Assert.Equal(6, sink.Count);

        // 1. Double
        var e1 = sink.Events[0];
        Assert.True(e1.HasProperty);
        Assert.Equal(1UL, e1.PropertyId);
        Assert.Equal("time-pos", e1.PropertyName);
        Assert.Equal(MpvFormat.Double, e1.PropertyFormat);
        Assert.True(e1.PropertyAvailable);
        Assert.Equal(123.456, e1.DoubleValue);
        Assert.Null(e1.Int64Value);
        Assert.Null(e1.FlagValue);
        Assert.Null(e1.StringValue);

        // 2. Int64
        var e2 = sink.Events[1];
        Assert.True(e2.HasProperty);
        Assert.Equal(2UL, e2.PropertyId);
        Assert.Equal("playlist-pos", e2.PropertyName);
        Assert.Equal(MpvFormat.Int64, e2.PropertyFormat);
        Assert.True(e2.PropertyAvailable);
        Assert.Equal(9999999999L, e2.Int64Value);
        Assert.Null(e2.DoubleValue);
        Assert.Null(e2.FlagValue);
        Assert.Null(e2.StringValue);

        // 3. Flag (true)
        var e3 = sink.Events[2];
        Assert.True(e3.HasProperty);
        Assert.Equal(3UL, e3.PropertyId);
        Assert.Equal("pause", e3.PropertyName);
        Assert.Equal(MpvFormat.Flag, e3.PropertyFormat);
        Assert.True(e3.PropertyAvailable);
        Assert.True(e3.FlagValue);

        // 4. Flag (false)
        var e4 = sink.Events[3];
        Assert.True(e4.HasProperty);
        Assert.Equal(4UL, e4.PropertyId);
        Assert.Equal("mute", e4.PropertyName);
        Assert.Equal(MpvFormat.Flag, e4.PropertyFormat);
        Assert.True(e4.PropertyAvailable);
        Assert.False(e4.FlagValue);

        // 5. String
        var e5 = sink.Events[4];
        Assert.True(e5.HasProperty);
        Assert.Equal(5UL, e5.PropertyId);
        Assert.Equal("media-title", e5.PropertyName);
        Assert.Equal(MpvFormat.String, e5.PropertyFormat);
        Assert.True(e5.PropertyAvailable);
        Assert.Equal("Aether 日本語 Test 🎬", e5.StringValue);

        // 6. Unavailable (Data null -> Format None)
        var e6 = sink.Events[5];
        Assert.True(e6.HasProperty);
        Assert.Equal(6UL, e6.PropertyId);
        Assert.Equal("demuxer-cache-state", e6.PropertyName);
        Assert.Equal(MpvFormat.None, e6.PropertyFormat);
        Assert.False(e6.PropertyAvailable);
        Assert.Null(e6.DoubleValue);
        Assert.Null(e6.Int64Value);
        Assert.Null(e6.FlagValue);
        Assert.Null(e6.StringValue);
    }

    [Fact]
    public void StartFile_And_EndFile_Payloads_DecodeCorrectly()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        source.EnqueueStartFile(playlistEntryId: 42);
        source.EnqueueEndFile(
            reason: MpvEndFileReason.Eof,
            error: 0,
            playlistEntryId: 42,
            playlistInsertId: 10,
            playlistInsertNumEntries: 1);

        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 2));
        Assert.True(loop.Stop(2000));

        var e1 = sink.Events[0];
        Assert.Equal(MpvEventId.StartFile, e1.Id);
        Assert.True(e1.HasStartFile);
        Assert.False(e1.HasEndFile);
        Assert.False(e1.HasProperty);
        Assert.Equal(42L, e1.StartFile.PlaylistEntryId);

        var e2 = sink.Events[1];
        Assert.Equal(MpvEventId.EndFile, e2.Id);
        Assert.True(e2.HasEndFile);
        Assert.False(e2.HasStartFile);
        Assert.False(e2.HasProperty);
        Assert.Equal(MpvEndFileReason.Eof, e2.EndFile.Reason);
        Assert.Equal(0, e2.EndFile.Error);
        Assert.Equal(42L, e2.EndFile.PlaylistEntryId);
        Assert.Equal(10L, e2.EndFile.PlaylistInsertId);
        Assert.Equal(1, e2.EndFile.PlaylistInsertNumEntries);
    }

    [Fact]
    public void Start_RunsOnThreadNamedAetherMpvEvents_And_IsEventThread_IsTrueOnlyThere()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);
        sink.Loop = loop;

        Assert.False(loop.IsEventThread);

        source.Enqueue(MpvEventId.Seek);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 1));
        Assert.True(loop.Stop(2000));

        Assert.Equal(MpvEventLoop.ThreadName, sink.Events[0].ThreadName);
        Assert.True(sink.Events[0].IsEventThread);
        Assert.False(loop.IsEventThread);
    }

    [Fact]
    public async Task Dispose_WakesParkedLoop_AndCompletionFinishes_AndReleaseCalledOnce()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        var loop = new MpvEventLoop(source, sink);

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.IsRunning));

        loop.Dispose();

        await loop.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(loop.IsRunning);
        Assert.Equal(1, source.AcquireCount);
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public void Stop_ReturnsTrue_WhenLoopExitsWithinTimeout()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.IsRunning));

        bool stopped = loop.Stop(2000);
        Assert.True(stopped);
        Assert.True(loop.Completion.IsCompleted);
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public async Task Stop_FromInsideOnEvent_ReturnsFalseWithoutDeadlocking_AndLoopExits()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);
        sink.Loop = loop;
        sink.CallStopOnEventIndex = 1;

        source.Enqueue(MpvEventId.Seek);
        loop.Start();

        await loop.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(loop.IsRunning);
        Assert.False(sink.StopResultFromInsideEvent);
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public async Task MarkingSourceClosed_And_TryWakeup_StopsLoop_MimickingMpvContextDispose()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.IsRunning));

        source.IsClosed = true;
        source.TryWakeup();

        await loop.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(loop.IsRunning);
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public async Task ShutdownEvent_IsDelivered_ThenLoopExits()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        source.Enqueue(MpvEventId.FileLoaded, replyUserdata: 1);
        source.EnqueueShutdown(replyUserdata: 2);
        source.Enqueue(MpvEventId.Seek, replyUserdata: 3);

        loop.Start();

        await loop.Completion.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(loop.IsRunning);

        Assert.Equal(2, sink.Count);
        Assert.Equal(MpvEventId.FileLoaded, sink.Events[0].Id);
        Assert.Equal(MpvEventId.Shutdown, sink.Events[1].Id);
        Assert.Equal(1, source.ReleaseCount);
    }

    [Fact]
    public void SinkFault_CallsOnFault_AndLoopKeepsDeliveringLaterEvents()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();

        var expectedException = new InvalidOperationException("Sink failed on event 1");
        sink.FaultOnIndex = 1;
        sink.ExceptionToThrow = expectedException;

        source.Enqueue(MpvEventId.FileLoaded, replyUserdata: 1);
        source.Enqueue(MpvEventId.Seek, replyUserdata: 2);
        source.Enqueue(MpvEventId.PlaybackRestart, replyUserdata: 3);

        using var loop = new MpvEventLoop(source, sink);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 2 && sink.Faults.Count == 1));
        Assert.True(loop.Stop(2000));

        Assert.Single(sink.Faults);
        Assert.Same(expectedException, sink.Faults[0]);
        Assert.Equal(2, sink.Count);
        Assert.Equal(MpvEventId.Seek, sink.Events[0].Id);
        Assert.Equal(MpvEventId.PlaybackRestart, sink.Events[1].Id);
        Assert.Equal(3, loop.EventsDispatched);
    }

    [Fact]
    public void SinkFault_ThrowingFromOnFaultToo_DoesNotKillLoop()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink
        {
            FaultOnIndex = 1,
            ExceptionToThrow = new InvalidOperationException("Sink failed on event 1"),
            ThrowFromOnFault = true
        };

        source.Enqueue(MpvEventId.FileLoaded, replyUserdata: 1);
        source.Enqueue(MpvEventId.Seek, replyUserdata: 2);

        using var loop = new MpvEventLoop(source, sink);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 1));
        Assert.True(loop.Stop(2000));

        Assert.Single(sink.Events);
        Assert.Equal(MpvEventId.Seek, sink.Events[0].Id);
        Assert.Equal(2, loop.EventsDispatched);
    }

    [Fact]
    public void Start_CalledTwice_ThrowsInvalidOperationException()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        loop.Start();
        Assert.Throws<InvalidOperationException>(() => loop.Start());
        Assert.True(loop.Stop(2000));
    }

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        loop.Dispose();
        Assert.Throws<ObjectDisposedException>(() => loop.Start());
        Assert.Equal(0, source.AcquireCount);
    }

    [Fact]
    public void Start_WhenTryAcquireReturnsFalse_ThrowsObjectDisposedException_AndStartsNoThread()
    {
        using var source = new FakeMpvEventSource { FailAcquire = true };
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        Assert.Throws<ObjectDisposedException>(() => loop.Start());
        Assert.False(loop.IsRunning);
        Assert.Equal(0, source.AcquireCount);
        Assert.Equal(0, source.ReleaseCount);
    }

    [Fact]
    public void NoneEvents_AreNotDelivered_AndNotCounted()
    {
        using var source = new FakeMpvEventSource();
        var sink = new TestEventSink();
        using var loop = new MpvEventLoop(source, sink);

        source.TryWakeup();
        source.EnqueueNone();
        source.Enqueue(MpvEventId.Seek, replyUserdata: 1);
        source.TryWakeup();
        source.EnqueueNone();
        source.Enqueue(MpvEventId.FileLoaded, replyUserdata: 2);
        source.TryWakeup();

        loop.Start();

        Assert.True(TestWait.ForCondition(() => sink.Count == 2));
        Assert.True(loop.Stop(2000));

        Assert.Equal(2, sink.Count);
        Assert.Equal(2, loop.EventsDispatched);
        Assert.Equal(MpvEventId.Seek, sink.Events[0].Id);
        Assert.Equal(MpvEventId.FileLoaded, sink.Events[1].Id);
    }

    [Fact]
    public async Task ZeroAllocation_InsideSinkAcross1000PropertyEvents()
    {
        using var source = new FakeMpvEventSource();
        var sink = new ZeroAllocSink();

        const int warmUpCount = 50;
        const int testCount = 1000;
        const int totalCount = warmUpCount + testCount;

        for (int i = 1; i <= totalCount; i++)
        {
            source.EnqueuePropertyDouble((ulong)i, "time-pos", i * 0.5);
        }

        using var loop = new MpvEventLoop(source, sink);
        loop.Start();

        await sink.Done.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.True(loop.Stop(2000));

        Assert.Equal(0, sink.Delta);
    }

    private sealed class ZeroAllocSink : IMpvEventSink
    {
        private int _count;
        private long _startBytes;
        private long _endBytes;
        private readonly TaskCompletionSource _done = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Done => _done.Task;
        public long Delta => _endBytes - _startBytes;

        public void OnEvent(MpvEventView mpvEvent)
        {
            if (mpvEvent.TryGetProperty(out var prop) && prop.TryGetDouble(out _))
            {
                _count++;
                if (_count == 50)
                {
                    _startBytes = GC.GetAllocatedBytesForCurrentThread();
                }
                else if (_count == 1050)
                {
                    _endBytes = GC.GetAllocatedBytesForCurrentThread();
                    _done.TrySetResult();
                }
            }
        }

        public void OnFault(Exception exception) { }
    }

    private sealed class RecordedEvent
    {
        public MpvEventId Id { get; init; }
        public int Error { get; init; }
        public ulong ReplyUserdata { get; init; }
        public bool HasProperty { get; init; }
        public ulong PropertyId { get; init; }
        public string? PropertyName { get; init; }
        public MpvFormat PropertyFormat { get; init; }
        public bool PropertyAvailable { get; init; }
        public double? DoubleValue { get; init; }
        public long? Int64Value { get; init; }
        public bool? FlagValue { get; init; }
        public string? StringValue { get; init; }
        public bool HasStartFile { get; init; }
        public MpvEventStartFile StartFile { get; init; }
        public bool HasEndFile { get; init; }
        public MpvEventEndFile EndFile { get; init; }
        public string? ThreadName { get; init; }
        public bool IsEventThread { get; init; }
    }

    private sealed class TestEventSink : IMpvEventSink
    {
        private readonly object _lock = new();
        private readonly List<RecordedEvent> _events = new();
        private readonly List<Exception> _faults = new();
        private int _dispatchIndex;

        public MpvEventLoop? Loop { get; set; }
        public int? FaultOnIndex { get; set; }
        public Exception? ExceptionToThrow { get; set; }
        public bool ThrowFromOnFault { get; set; }
        public int? CallStopOnEventIndex { get; set; }
        public bool? StopResultFromInsideEvent { get; private set; }

        public int Count
        {
            get
            {
                lock (_lock) return _events.Count;
            }
        }

        public IReadOnlyList<RecordedEvent> Events
        {
            get
            {
                lock (_lock) return _events.ToArray();
            }
        }

        public IReadOnlyList<Exception> Faults
        {
            get
            {
                lock (_lock) return _faults.ToArray();
            }
        }

        public void OnEvent(MpvEventView mpvEvent)
        {
            int currentIndex = Interlocked.Increment(ref _dispatchIndex);

            if (CallStopOnEventIndex == currentIndex && Loop != null)
            {
                StopResultFromInsideEvent = Loop.Stop(2000);
            }

            if (FaultOnIndex == currentIndex && ExceptionToThrow != null)
            {
                throw ExceptionToThrow;
            }

            bool hasProp = mpvEvent.TryGetProperty(out var prop);
            ulong propId = 0;
            string? propName = null;
            MpvFormat propFormat = MpvFormat.None;
            bool propAvailable = false;
            double? dVal = null;
            long? iVal = null;
            bool? fVal = null;
            string? sVal = null;

            if (hasProp)
            {
                propId = prop.Id;
                propName = prop.NameUtf8.Length > 0 ? Encoding.UTF8.GetString(prop.NameUtf8) : null;
                propFormat = prop.Format;
                propAvailable = prop.IsAvailable;
                if (prop.TryGetDouble(out var d)) dVal = d;
                if (prop.TryGetInt64(out var i)) iVal = i;
                if (prop.TryGetFlag(out var f)) fVal = f;
                if (prop.TryGetStringUtf8(out var s)) sVal = Encoding.UTF8.GetString(s);
            }

            bool hasStart = mpvEvent.TryGetStartFile(out var startFile);
            bool hasEnd = mpvEvent.TryGetEndFile(out var endFile);

            var recorded = new RecordedEvent
            {
                Id = mpvEvent.Id,
                Error = mpvEvent.Error,
                ReplyUserdata = mpvEvent.ReplyUserdata,
                HasProperty = hasProp,
                PropertyId = propId,
                PropertyName = propName,
                PropertyFormat = propFormat,
                PropertyAvailable = propAvailable,
                DoubleValue = dVal,
                Int64Value = iVal,
                FlagValue = fVal,
                StringValue = sVal,
                HasStartFile = hasStart,
                StartFile = startFile,
                HasEndFile = hasEnd,
                EndFile = endFile,
                ThreadName = Thread.CurrentThread.Name,
                IsEventThread = Loop?.IsEventThread ?? false
            };

            lock (_lock)
            {
                _events.Add(recorded);
            }
        }

        public void OnFault(Exception exception)
        {
            lock (_lock)
            {
                _faults.Add(exception);
            }

            if (ThrowFromOnFault)
            {
                throw new ApplicationException("Fault from OnFault");
            }
        }
    }
}
