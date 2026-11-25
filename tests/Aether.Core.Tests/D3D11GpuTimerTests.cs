using System;
using System.Runtime.InteropServices;
using System.Threading;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public unsafe class D3D11GpuTimerTests
{
    private static bool HasD3D11Device()
    {
        try
        {
            using var device = D3D11DeviceManager.Create();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public void QueryStructs_HaveExactNativeAbiLayout()
    {
        Assert.Equal(8, sizeof(D3D11QueryDesc));
        Assert.Equal(16, sizeof(D3D11QueryDataTimestampDisjoint));
    }

    [Fact]
    public void TryCreate_AndDispose_ReleasesDeviceReference()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        nint rawDevice = device.DevicePointer;
        Marshal.AddRef(rawDevice);

        using D3D11GpuTimer? timer = D3D11GpuTimer.TryCreate(device);
        Assert.NotNull(timer);
        Assert.Equal(-1.0, timer.LastFrameMilliseconds);
        Assert.Equal(-1.0, timer.AverageFrameMilliseconds);
        Assert.Equal(-1.0, timer.MaxFrameMilliseconds);
        Assert.Equal(0, timer.SampleCount);

        timer.Dispose();
        timer.Dispose(); // double-dispose safety
        device.Dispose();

        // Only our own AddRef should remain.
        Assert.Equal(0, Marshal.Release(rawDevice));
    }

    [Fact]
    public void FrameBracket_ProducesValidNonNegativeDuration()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { Width = 128, Height = 128 });
        using D3D11GpuTimer? timer = D3D11GpuTimer.TryCreate(device);
        Assert.NotNull(timer);

        long deadline = Environment.TickCount64 + 3000;
        while (timer.SampleCount == 0 && Environment.TickCount64 < deadline)
        {
            bool started = timer.TryBeginFrame();
            presenter.Clear(0.1f, 0.2f, 0.3f, 1f);
            if (started)
            {
                timer.EndFrame();
            }
            presenter.Present(vsync: false);
            Thread.Sleep(1);
        }

        Assert.True(timer.SampleCount > 0, "Expected at least one completed D3D11 timestamp sample.");
        Assert.True(timer.LastFrameMilliseconds >= 0 && double.IsFinite(timer.LastFrameMilliseconds));
        Assert.True(timer.AverageFrameMilliseconds >= 0 && double.IsFinite(timer.AverageFrameMilliseconds));
        Assert.True(timer.MaxFrameMilliseconds >= timer.AverageFrameMilliseconds - 1e-6);
    }

    [Fact]
    public void HotPath_ZeroAllocations_Over100Frames()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        using var device = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(device, new DxgiPresenterOptions { Width = 64, Height = 64 });
        using D3D11GpuTimer? timer = D3D11GpuTimer.TryCreate(device);
        Assert.NotNull(timer);

        // Warm up once before measuring.
        for (int i = 0; i < 8; i++)
        {
            bool started = timer.TryBeginFrame();
            presenter.Clear(0f, 0f, 0f, 1f);
            if (started)
            {
                timer.EndFrame();
            }
            presenter.Present(vsync: false);
        }

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            bool started = timer.TryBeginFrame();
            presenter.Clear(0f, 0f, 0f, 1f);
            if (started)
            {
                timer.EndFrame();
            }
            presenter.Present(vsync: false);
        }
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, allocAfter - allocBefore);
    }

    [Fact]
    public void RenderLoop_PopulatesGpuFrameTimeMilliseconds()
    {
        if (!HasD3D11Device())
        {
            return;
        }

        using var host = new TestRenderLoopHost();
        using var loop = new MpvRenderLoop(null, host, new MpvRenderLoopOptions { VSync = false });
        loop.SetTargetSize(128, 128, 1f, 1f);
        loop.Start();

        long deadline = Environment.TickCount64 + 3000;
        while (loop.GpuTimingSamples == 0 && Environment.TickCount64 < deadline)
        {
            loop.RequestRedraw();
            Thread.Sleep(2);
        }

        Assert.True(loop.GpuTimingSamples > 0);
        Assert.True(loop.GpuFrameTimeMilliseconds >= 0);
        Assert.True(loop.AverageGpuFrameTimeMilliseconds >= 0);
        Assert.True(loop.MaxGpuFrameTimeMilliseconds >= 0);

        Assert.True(loop.Stop(3000));
    }
}
