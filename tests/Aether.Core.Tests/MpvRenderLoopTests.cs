using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class MpvRenderLoopTests
{
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);

    [Fact]
    public void Constructor_NullHost_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MpvRenderLoop(null, null!));
    }

    [Fact]
    public void Start_CalledTwice_ThrowsInvalidOperationException()
    {
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(null, host, new MpvRenderLoopOptions { VSync = false });

        loop.Start();
        Assert.Throws<InvalidOperationException>(() => loop.Start());

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void Stop_BeforeStart_ReturnsTrue_AndSetsStateStopped()
    {
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(null, host, new MpvRenderLoopOptions { VSync = false });

        Assert.Equal(RenderLoopState.NotStarted, loop.State);
        bool stopped = loop.Stop(1000);

        Assert.True(stopped);
        Assert.Equal(RenderLoopState.Stopped, loop.State);
    }

    [Fact]
    public void Start_OnSwapChainCreatedGenerationOne_StateRunning_FramesPresentedGrows()
    {
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(null, host, new MpvRenderLoopOptions { VSync = false });

        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running));
        Assert.True(TestWait.ForCondition(() => host.CreatedHandoffs.Count == 1));
        Assert.True(host.CreatedHandoffs.TryPeek(out var handoff));
        Assert.Equal(1, handoff.Generation);
        Assert.NotEqual(0, handoff.SwapChain);
        Assert.Equal(1, loop.Generation);

        long initialFrames = loop.FramesPresented;
        long deadline = Environment.TickCount64 + 3000;
        while (loop.FramesPresented == initialFrames && Environment.TickCount64 < deadline)
        {
            loop.RequestRedraw();
            Thread.Sleep(5);
        }
        Assert.True(loop.FramesPresented > initialFrames);

        Assert.True(loop.Stop(3000));
        Assert.Equal(RenderLoopState.Stopped, loop.State);
    }

    [Fact]
    public void RedrawWithoutANewVideoFrame_Renders_ButDoesNotReportASwapToMpv()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { VSync = false });

        loop.SetTargetSize(64, 64, 1.0f, 1.0f);
        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        var backend = Assert.Single(factory.Backends);

        long before = loop.FramesPresented;
        loop.RequestRedraw();

        Assert.True(TestWait.ForCondition(() => loop.FramesPresented > before));
        Assert.True(backend.RenderCount >= 2);
        Assert.Equal(0, backend.ReportSwapCount);
        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void SetTargetSize_DifferentPixelSize_CallsReleaseThenAcquireInOrder()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { VSync = false });

        loop.SetTargetSize(64, 64, 1.0f, 1.0f);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        var backend = Assert.Single(factory.Backends);

        loop.SetTargetSize(128, 128, 1.0f, 1.0f);
        loop.RequestRedraw();

        Assert.True(TestWait.ForCondition(() => backend.ReleaseCount >= 1 && backend.AcquireCount >= 1));

        var eventList = backend.Events.ToList();
        int firstRelease = eventList.IndexOf("Release");
        int firstAcquire = eventList.IndexOf("Acquire");

        Assert.True(firstRelease >= 0, "ReleaseBackBuffer was not called.");
        Assert.True(firstAcquire > firstRelease, "AcquireBackBuffer was not called after ReleaseBackBuffer.");

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void SimulatedLoss_RenderReturnsFalse_RecoversWithGenerationTwo()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            RetryDelayMilliseconds = 20,
        };
        using var loop = new MpvRenderLoop(factory, host, options);

        int failRenderOnce = 0;
        bool firstBackendDisposedBeforeSecondCreated = false;

        factory.RenderCallback = backend =>
        {
            if (Interlocked.CompareExchange(ref failRenderOnce, 0, 1) == 1)
            {
                return false;
            }
            backend.Presenter.Clear(0f, 0f, 0f, 1f);
            return true;
        };

        factory.OnCreateCallback = _ =>
        {
            if (factory.CreateCount == 2)
            {
                var first = factory.Backends[0];
                firstBackendDisposedBeforeSecondCreated = (first.DisposeCount == 1);
            }
        };

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        Assert.Equal(1, loop.Generation);

        Volatile.Write(ref failRenderOnce, 1);
        loop.RequestRedraw();

        Assert.True(TestWait.ForCondition(() => loop.Generation == 2 && loop.DeviceLossCount == 1 && loop.State == RenderLoopState.Running));

        Assert.True(firstBackendDisposedBeforeSecondCreated);
        var lostEvent = Assert.Single(host.DeviceLostEvents);
        Assert.Equal(DXGI_ERROR_DEVICE_REMOVED, lostEvent.Reason);
        Assert.Equal(1, lostEvent.Generation);
        Assert.Equal(1, loop.DeviceLossCount);
        Assert.Equal(2, loop.Generation);
        Assert.Equal(2, host.CreatedHandoffs.Count);

        var handoffs = host.CreatedHandoffs.ToArray();
        Assert.Equal(1, handoffs[0].Generation);
        Assert.Equal(2, handoffs[1].Generation);

        long framesGen2 = loop.FramesPresented;
        loop.RequestRedraw();
        Assert.True(TestWait.ForCondition(() => loop.FramesPresented > framesGen2));

        Assert.True(loop.Stop(3000));
        Assert.Equal(RenderLoopState.Stopped, loop.State);

        int totalCreated = factory.CreateCount;
        int totalDisposed = factory.Backends.Sum(b => b.DisposeCount);
        Assert.Equal(totalCreated, totalDisposed);
    }

    [Fact]
    public void DeviceFactory_ThrowsFirstTwoTimes_CallsOnFaultTwice_ThenRunsWithGenerationOne()
    {
        using var host = new TestRenderLoopHost();
        int attempts = 0;
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            RetryDelayMilliseconds = 20,
            MaxRetryDelayMilliseconds = 100,
            DeviceFactory = opt =>
            {
                if (Interlocked.Increment(ref attempts) <= 2)
                {
                    throw new InvalidOperationException("Simulated device creation failure");
                }
                return D3D11DeviceManager.Create(opt);
            }
        };
        using var loop = new MpvRenderLoop(null, host, options);

        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running, 4000));
        Assert.True(host.FaultEvents.Count >= 2);
        Assert.Equal(1, loop.Generation);
        Assert.Equal(RenderLoopState.Running, loop.State);

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void Host_OnSwapChainCreatedThrows_LoopKeepsRendering_AndOnFaultReceived()
    {
        using var host = new TestRenderLoopHost
        {
            OnCreatedAction = _ => throw new InvalidOperationException("Host rejected handoff")
        };
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            RetryDelayMilliseconds = 20,
        };
        using var loop = new MpvRenderLoop(null, host, options);

        loop.Start();

        Assert.True(TestWait.ForCondition(() => host.FaultEvents.Count >= 1));

        long deadline = Environment.TickCount64 + 3000;
        while (loop.FramesPresented < 5 && Environment.TickCount64 < deadline)
        {
            loop.RequestRedraw();
            Thread.Sleep(5);
        }

        Assert.True(loop.FramesPresented >= 5);
        Assert.NotEmpty(host.FaultEvents);

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void ZeroAllocation_RenderThread_BetweenFramesTenAndSixty()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
        };

        int frameIndex = 0;
        long allocAtFrame10 = 0;
        long allocAtFrame60 = 0;

        factory.RenderCallback = backend =>
        {
            int current = Interlocked.Increment(ref frameIndex);
            if (current == 10)
            {
                allocAtFrame10 = GC.GetAllocatedBytesForCurrentThread();
            }
            else if (current == 60)
            {
                allocAtFrame60 = GC.GetAllocatedBytesForCurrentThread();
            }
            backend.Presenter.Clear(0f, 0f, 0f, 1f);
            return true;
        };

        using var loop = new MpvRenderLoop(factory, host, options);
        loop.Start();

        long deadline = Environment.TickCount64 + 4000;
        while (Volatile.Read(ref frameIndex) < 65 && Environment.TickCount64 < deadline)
        {
            loop.RequestRedraw();
            Thread.Sleep(2);
        }

        Assert.True(Volatile.Read(ref frameIndex) >= 60);
        Assert.Equal(0, allocAtFrame60 - allocAtFrame10);

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void SwapChainHandoff_Dispose_SetsSwapChainZero_AndDoubleDisposeIsSafe()
    {
        nint initialPtr = 0;
        nint afterFirstDispose = -1;
        nint afterSecondDispose = -1;
        bool callbackInvoked = false;

        using var host = new TestRenderLoopHost
        {
            OnCreatedAction = handoff =>
            {
                initialPtr = handoff.SwapChain;
                handoff.Dispose();
                afterFirstDispose = handoff.SwapChain;
                handoff.Dispose();
                afterSecondDispose = handoff.SwapChain;
                callbackInvoked = true;
            }
        };

        var options = new MpvRenderLoopOptions { VSync = false };
        using var loop = new MpvRenderLoop(null, host, options);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => callbackInvoked));
        Assert.NotEqual(0, initialPtr);
        Assert.Equal(0, afterFirstDispose);
        Assert.Equal(0, afterSecondDispose);

        Assert.True(loop.Stop(3000));
    }

    private static bool HasD3D11Device()
    {
        try
        {
            using var manager = D3D11DeviceManager.Create();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void PresenterSwitch_BurstOfResizes_KeepsGenerationOneAndBalancedReleaseAcquire()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { VSync = false });

        loop.SetTargetSize(1518, 854, 1.0f, 1.0f);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        var backend = Assert.Single(factory.Backends);

        // Burst of SetTargetSize calls like a presenter switch into PiP
        loop.SetTargetSize(1518, 854, 1.0f, 1.0f);
        loop.SetTargetSize(400, 225, 1.0f, 1.0f);
        loop.SetTargetSize(384, 217, 1.0f, 1.0f);
        loop.SetTargetSize(400, 225, 1.0f, 1.0f);

        Assert.True(TestWait.ForCondition(() => backend.Presenter.PixelWidth == 400 && backend.Presenter.PixelHeight == 225));

        // Switch back to 1518x854
        loop.SetTargetSize(1518, 854, 1.0f, 1.0f);
        Assert.True(TestWait.ForCondition(() => backend.Presenter.PixelWidth == 1518 && backend.Presenter.PixelHeight == 854));
        Assert.True(TestWait.ForCondition(() => backend.ReleaseCount > 0 && backend.ReleaseCount == backend.AcquireCount));

        Assert.Equal(1, loop.Generation);
        Assert.Equal(0, loop.DeviceLossCount);
        Assert.Equal(RenderLoopState.Running, loop.State);

        var events = backend.Events.Where(e => e is "Release" or "Acquire").ToList();
        Assert.NotEmpty(events);
        Assert.Equal(0, events.Count % 2);
        for (int i = 0; i < events.Count; i += 2)
        {
            Assert.Equal("Release", events[i]);
            Assert.Equal("Acquire", events[i + 1]);
        }

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void PresenterSwitch_PausedRedrawAfterResize_PresentsFrameWithoutNewVideoFrames()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { VSync = false });

        loop.SetTargetSize(1518, 854, 1.0f, 1.0f);
        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        var backend = Assert.Single(factory.Backends);

        // Switch to PiP size
        loop.SetTargetSize(400, 225, 1.0f, 1.0f);
        Assert.True(TestWait.ForCondition(() => backend.Presenter.PixelWidth == 400 && backend.Presenter.PixelHeight == 225));
        Assert.True(TestWait.ForCondition(() => backend.ReleaseCount > 0 && backend.ReleaseCount == backend.AcquireCount));

        // Request a redraw while "paused" (backend PollFrame returns false, so no new video frames)
        long framesBefore = loop.FramesPresented;
        loop.RequestRedraw();

        Assert.True(TestWait.ForCondition(() => loop.FramesPresented > framesBefore));
        Assert.Equal(1, loop.Generation);
        Assert.Equal(0, loop.DeviceLossCount);
        Assert.Equal(RenderLoopState.Running, loop.State);
        Assert.Equal(backend.ReleaseCount, backend.AcquireCount);
        Assert.Equal(400u, backend.Presenter.PixelWidth);
        Assert.Equal(225u, backend.Presenter.PixelHeight);

        Assert.True(loop.Stop(3000));
    }

    [Fact]
    public void Stop_FreesTheBackendBeforeItsDevice_AndEachOnlyOnce()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        var devices = new ConcurrentQueue<D3D11DeviceManager>();
        bool? deviceAliveAtBackendDispose = null;
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            DeviceFactory = opt =>
            {
                D3D11DeviceManager device = D3D11DeviceManager.Create(opt);
                devices.Enqueue(device);
                return device;
            },
        };
        // The backend frees the mpv render context, which needs the device it was created on.
        factory.OnDisposeCallback = _ => deviceAliveAtBackendDispose = devices.TryPeek(out D3D11DeviceManager? d) && !d.IsDisposed;
        using var loop = new MpvRenderLoop(factory, host, options);

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running));
        Assert.True(loop.Stop(3000));

        Assert.True(deviceAliveAtBackendDispose);
        Assert.True(Assert.Single(devices).IsDisposed);
        Assert.Equal(1, Assert.Single(factory.Backends).DisposeCount);
    }

    [Fact]
    public void DeviceLoss_WhenFreeingTheBackendThrows_StillRecovers_WithoutFreeingAnythingTwice()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        var devices = new ConcurrentQueue<D3D11DeviceManager>();
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            RetryDelayMilliseconds = 20,
            DeviceFactory = opt =>
            {
                D3D11DeviceManager device = D3D11DeviceManager.Create(opt);
                devices.Enqueue(device);
                return device;
            },
        };
        int failRenderOnce = 0;
        factory.RenderCallback = backend =>
        {
            if (Interlocked.CompareExchange(ref failRenderOnce, 0, 1) == 1)
            {
                return false;
            }
            backend.Presenter.Clear(0f, 0f, 0f, 1f);
            return true;
        };
        // A lost GPU can make teardown fail; the first backend's Dispose throws.
        factory.OnDisposeCallback = backend =>
        {
            if (ReferenceEquals(backend, factory.Backends[0]))
            {
                throw new InvalidOperationException("Simulated teardown failure on a lost device");
            }
        };
        using var loop = new MpvRenderLoop(factory, host, options);

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        Volatile.Write(ref failRenderOnce, 1);
        loop.RequestRedraw();

        Assert.True(TestWait.ForCondition(() => loop.Generation == 2 && loop.State == RenderLoopState.Running));
        Assert.Contains(host.FaultEvents, e => e.Message.Contains("Simulated teardown failure"));
        D3D11DeviceManager[] created = devices.ToArray();
        Assert.True(created[0].IsDisposed);

        Assert.True(loop.Stop(3000));
        Assert.All(factory.Backends, b => Assert.Equal(1, b.DisposeCount));
        Assert.All(devices, d => Assert.True(d.IsDisposed));
    }

    [Fact]
    public void Dispose_ReturnsAtOnce_AndTheRenderThreadStillTearsItsStackDown()
    {
        var factory = new TestBackendFactory();
        using var host = new TestRenderLoopHost();
        var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { VSync = false });

        loop.Start();
        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running));
        loop.Dispose();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Stopped));
        Assert.Equal(1, Assert.Single(factory.Backends).DisposeCount);
    }

    private sealed class TestBackendFactory : IVideoRenderBackendFactory
    {
        private readonly List<TestBackend> _backends = new();
        private readonly Lock _lock = new();

        public bool CachesDeviceState { get; set; } = false;
        public Func<TestBackend, bool>? RenderCallback { get; set; }
        public Action<TestBackend>? OnCreateCallback { get; set; }
        public Action<TestBackend>? OnDisposeCallback { get; set; }

        public int CreateCount
        {
            get
            {
                lock (_lock) return _backends.Count;
            }
        }

        public List<TestBackend> Backends
        {
            get
            {
                lock (_lock) return new List<TestBackend>(_backends);
            }
        }

        public IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent)
        {
            var backend = new TestBackend(presenter, this);
            lock (_lock)
            {
                _backends.Add(backend);
            }
            OnCreateCallback?.Invoke(backend);
            return backend;
        }
    }

    private sealed class TestBackend : IVideoRenderBackend
    {
        private readonly DxgiPresenter _presenter;
        private readonly TestBackendFactory _factory;
        private int _disposeCount;
        private int _renderCount;
        private int _reportSwapCount;
        private int _releaseCount;
        private int _acquireCount;
        private readonly ConcurrentQueue<string> _events = new();

        public TestBackend(DxgiPresenter presenter, TestBackendFactory factory)
        {
            _presenter = presenter;
            _factory = factory;
        }

        public DxgiPresenter Presenter => _presenter;
        public int DisposeCount => Volatile.Read(ref _disposeCount);
        public int RenderCount => Volatile.Read(ref _renderCount);
        public int ReportSwapCount => Volatile.Read(ref _reportSwapCount);
        public int ReleaseCount => Volatile.Read(ref _releaseCount);
        public int AcquireCount => Volatile.Read(ref _acquireCount);
        public ConcurrentQueue<string> Events => _events;

        public bool PollFrame() => false;

        public void ReleaseBackBuffer()
        {
            Interlocked.Increment(ref _releaseCount);
            _events.Enqueue("Release");
        }

        public void AcquireBackBuffer()
        {
            Interlocked.Increment(ref _acquireCount);
            _events.Enqueue("Acquire");
        }

        public bool Render(uint width, uint height)
        {
            Interlocked.Increment(ref _renderCount);
            if (_factory.RenderCallback != null)
            {
                return _factory.RenderCallback(this);
            }
            _presenter.Clear(0f, 0f, 0f, 1f);
            return true;
        }

        public void ReportSwap()
        {
            Interlocked.Increment(ref _reportSwapCount);
        }

        public void Dispose()
        {
            Interlocked.Increment(ref _disposeCount);
            _events.Enqueue("Dispose");
            _factory.OnDisposeCallback?.Invoke(this);
        }
    }
}
