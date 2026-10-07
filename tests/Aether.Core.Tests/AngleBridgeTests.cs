using System;
using System.Threading;
using Aether.Core.Interop.Angle;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class AngleBridgeTests
{
    [Fact]
    public void Create_OnHardwareAndWarp_SucceedsWithValidHandles()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using (var hardwareDevice = D3D11DeviceManager.Create())
        using (var presenter = new DxgiPresenter(hardwareDevice, new DxgiPresenterOptions { ClearStateOnResize = false }))
        using (var bridge = AngleBridge.Create(hardwareDevice, presenter))
        {
            Assert.NotEqual(0, bridge.Display);
            Assert.NotEqual(0, bridge.Context);
            Assert.NotEqual(0, bridge.Surface);
            Assert.False(bridge.IsDisposed);
        }

        var warpOptions = new D3D11DeviceOptions
        {
            DriverType = D3DDriverType.Warp,
            AllowWarpFallback = false,
        };
        using (var warpDevice = D3D11DeviceManager.Create(warpOptions))
        using (var presenter = new DxgiPresenter(warpDevice, new DxgiPresenterOptions { ClearStateOnResize = false }))
        using (var bridge = AngleBridge.Create(warpDevice, presenter))
        {
            Assert.NotEqual(0, bridge.Display);
            Assert.NotEqual(0, bridge.Context);
            Assert.NotEqual(0, bridge.Surface);
            Assert.False(bridge.IsDisposed);
        }
    }

    [Fact]
    public unsafe void GetProcAddressCallback_ReturnsValidPointerForGlFlush_AndNullForNullName()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { ClearStateOnResize = false });
        using var bridge = AngleBridge.Create(device, presenter);

        delegate* unmanaged[Cdecl]<void*, byte*, void*> callback = AngleBridge.GetProcAddressCallback;
        void* ctx = bridge.GetProcAddressContext;

        byte[] glFlushBytes = "glFlush\0"u8.ToArray();
        fixed (byte* pName = glFlushBytes)
        {
            void* proc = callback(ctx, pName);
            Assert.True(proc != null);
        }

        void* nullProc = callback(ctx, null);
        Assert.True(nullProc == null);
    }

    [Fact]
    public void ClearFlushPresent_ReturnsOk()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { ClearStateOnResize = false });
        using var bridge = AngleBridge.Create(device, presenter);

        bridge.Clear(0.2f, 0.4f, 0.6f, 1.0f);
        bridge.Flush();
        PresentResult result = presenter.Present(vsync: false);
        Assert.True(result is PresentResult.Ok or PresentResult.Occluded);
        Assert.False(presenter.IsDeviceLost);
    }

    [Fact]
    public void ReleaseBackBuffer_SetsSurfaceZero_ResizeSucceeds_AndAcquireRestoresSurface()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { ClearStateOnResize = false });
        using var bridge = AngleBridge.Create(device, presenter);

        Assert.NotEqual(0, bridge.Surface);

        bridge.ReleaseBackBuffer();
        Assert.Equal(0, bridge.Surface);

        presenter.SetTargetSize(256, 128, 1.0f, 1.0f);
        bool resized = presenter.ApplyPendingResize();
        Assert.True(resized);
        Assert.Equal(256u, presenter.PixelWidth);
        Assert.Equal(128u, presenter.PixelHeight);

        bridge.AcquireBackBuffer();
        Assert.NotEqual(0, bridge.Surface);
    }

    [Fact]
    public void ZeroAllocation_ClearFlushIsContextLostPresent_OverHundredFrames()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { ClearStateOnResize = false });
        using var bridge = AngleBridge.Create(device, presenter);

        for (int i = 0; i < 5; i++)
        {
            bridge.Clear(0.1f, 0.2f, 0.3f, 1.0f);
            bridge.Flush();
            _ = bridge.IsContextLost;
            presenter.Present(vsync: false);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            bridge.Clear(0.1f, 0.2f, 0.3f, 1.0f);
            bridge.Flush();
            _ = bridge.IsContextLost;
            presenter.Present(vsync: false);
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Dispose_TwiceIsSafe_AndSetsIsDisposedTrue()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { ClearStateOnResize = false });
        var bridge = AngleBridge.Create(device, presenter);

        Assert.False(bridge.IsDisposed);
        bridge.Dispose();
        Assert.True(bridge.IsDisposed);
        Assert.Equal(0, bridge.Surface);

        bridge.Dispose();
        Assert.True(bridge.IsDisposed);
    }

    [Fact]
    public void MpvRenderLoop_WithAngleBackend_Presents_Resizes_AndRecoversFromLoss()
    {
        if (!AngleTestEnvironment.IsAvailable)
        {
            return;
        }

        var baseFactory = new AngleMpvRenderBackendFactory(null);
        var factory = new LossSimulatingAngleBackendFactory(baseFactory);
        using var host = new TestRenderLoopHost();
        var options = new MpvRenderLoopOptions
        {
            VSync = false,
            RetryDelayMilliseconds = 20,
        };
        using var loop = new MpvRenderLoop(factory, host, options);

        loop.Start();

        Assert.True(TestWait.ForCondition(() => loop.State == RenderLoopState.Running && loop.FramesPresented >= 1));
        Assert.Equal(1, loop.Generation);

        long framesBeforeResize = loop.FramesPresented;
        loop.SetTargetSize(160, 90, 1.0f, 1.0f);
        loop.RequestRedraw();
        Assert.True(TestWait.ForCondition(() => loop.FramesPresented > framesBeforeResize));

        factory.FailRenderOnce = 1;
        loop.RequestRedraw();
        Assert.True(TestWait.ForCondition(() => loop.Generation == 2 && loop.State == RenderLoopState.Running));

        long framesAfterRecovery = loop.FramesPresented;
        loop.RequestRedraw();
        Assert.True(TestWait.ForCondition(() => loop.FramesPresented > framesAfterRecovery));

        Assert.True(loop.Stop(3000));
        Assert.Equal(RenderLoopState.Stopped, loop.State);

        Assert.Equal(2, loop.Generation);
        Assert.Equal(1, loop.DeviceLossCount);
        Assert.Empty(host.FaultEvents);
        Assert.Single(host.DeviceLostEvents);
        Assert.Equal(2, host.CreatedHandoffs.Count);
    }

    private sealed class LossSimulatingAngleBackendFactory(IVideoRenderBackendFactory inner) : IVideoRenderBackendFactory
    {
        public bool CachesDeviceState => inner.CachesDeviceState;
        public int FailRenderOnce;

        public IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent)
        {
            var backend = inner.Create(device, presenter, frameReadyEvent);
            return new LossSimulatingBackend(backend, this);
        }

        private sealed class LossSimulatingBackend(IVideoRenderBackend inner, LossSimulatingAngleBackendFactory factory) : IVideoRenderBackend
        {
            public bool PollFrame() => inner.PollFrame();
            public void ReleaseBackBuffer() => inner.ReleaseBackBuffer();
            public void AcquireBackBuffer() => inner.AcquireBackBuffer();
            public bool Render(uint width, uint height)
            {
                if (Interlocked.Exchange(ref factory.FailRenderOnce, 0) == 1)
                {
                    return false;
                }
                return inner.Render(width, height);
            }
            public void ReportSwap() => inner.ReportSwap();
            public void Dispose() => inner.Dispose();
        }
    }
}
