using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests;

public class MpvCoreShutdownTests
{
    /// <summary>Stands in for a core: its release blocks until allowed, like a slow <c>mpv_terminate_destroy</c>.</summary>
    private sealed class SlowHandle : SafeHandle
    {
        private readonly MpvCoreShutdown _shutdown;

        public SlowHandle(MpvCoreShutdown shutdown) : base(IntPtr.Zero, ownsHandle: true)
        {
            _shutdown = shutdown;
            SetHandle(1);
        }

        public ManualResetEventSlim Allow { get; } = new();
        public ManualResetEventSlim Started { get; } = new();
        public int ReleaseThreadId { get; private set; }
        public string? ReleaseThreadName { get; private set; }

        public override bool IsInvalid => handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            ReleaseThreadId = Environment.CurrentManagedThreadId;
            ReleaseThreadName = Thread.CurrentThread.Name;
            Started.Set();
            Allow.Wait(TimeSpan.FromSeconds(10));
            _shutdown.End();
            return true;
        }
    }

    [Fact]
    public void ReleaseOnWorker_DestroysOffTheCallingThread_WithoutWaitingForIt()
    {
        var shutdown = new MpvCoreShutdown();
        var handle = new SlowHandle(shutdown);
        bool added = false;
        handle.DangerousAddRef(ref added);
        shutdown.Begin();
        handle.Dispose(); // closed, but still pinned: nothing released yet
        Assert.False(handle.Started.IsSet);

        shutdown.ReleaseOnWorker(handle);

        Assert.True(handle.Started.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotEqual(Environment.CurrentManagedThreadId, handle.ReleaseThreadId);
        Assert.Equal(MpvCoreShutdown.ThreadName, handle.ReleaseThreadName);
        Assert.Equal(1, shutdown.Pending);
        Assert.False(shutdown.WaitForAll(0));

        handle.Allow.Set();
        Assert.True(shutdown.WaitForAll(5000));
        Assert.Equal(0, shutdown.Pending);
    }

    [Fact]
    public void ReleaseOnWorker_LeavesTheHandleAliveWhileAnotherOwnerHoldsIt()
    {
        var shutdown = new MpvCoreShutdown();
        var handle = new SlowHandle(shutdown);
        handle.Allow.Set();
        bool ours = false, renderContext = false;
        handle.DangerousAddRef(ref ours);
        handle.DangerousAddRef(ref renderContext);
        shutdown.Begin();
        handle.Dispose();

        shutdown.ReleaseOnWorker(handle);

        // The render context's reference keeps the core alive until it lets go, in that order.
        Assert.False(shutdown.WaitForAll(100));
        Assert.False(handle.Started.IsSet);
        handle.DangerousRelease();
        Assert.True(shutdown.WaitForAll(5000));
    }

    [Fact]
    public void WaitForAll_IsBounded_WhenADestructionNeverFinishes()
    {
        var shutdown = new MpvCoreShutdown();
        shutdown.Begin();

        var stopwatch = Stopwatch.StartNew();
        Assert.False(shutdown.WaitForAll(100));

        Assert.InRange(stopwatch.ElapsedMilliseconds, 50, 5000);
    }

    [Fact]
    public void WaitForAll_ReturnsAtOnce_WhenNothingIsPending()
    {
        Assert.True(new MpvCoreShutdown().WaitForAll(0));
    }
}
