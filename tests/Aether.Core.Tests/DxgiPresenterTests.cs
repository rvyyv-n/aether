using System;
using System.Runtime.InteropServices;
using Aether.Core.Interop.DirectX;
using Xunit;

namespace Aether.Core.Tests;

public class DxgiPresenterTests
{
    [Fact]
    public void GetDescription_MatchesExpectedConfiguration()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        DxgiSwapChainDesc1 desc = presenter.GetDescription();

        Assert.Equal(DxgiSwapEffect.FlipDiscard, desc.SwapEffect);
        Assert.Equal(DxgiScaling.Stretch, desc.Scaling);
        Assert.Equal(DxgiFormat.B8G8R8A8Unorm, desc.Format);
        Assert.Equal(2u, desc.BufferCount);
        Assert.Equal(1u, desc.SampleCount);
        Assert.Equal(DxgiAlphaMode.Ignore, desc.AlphaMode);
        Assert.True((desc.Flags & DxgiSwapChainFlags.FrameLatencyWaitableObject) != 0);

        if (manager.SupportsTearing)
        {
            Assert.True((desc.Flags & DxgiSwapChainFlags.AllowTearing) != 0);
            Assert.True(presenter.TearingEnabled);
        }
        else
        {
            Assert.False((desc.Flags & DxgiSwapChainFlags.AllowTearing) != 0);
            Assert.False(presenter.TearingEnabled);
        }
    }

    [Fact]
    public void GetDescription_WhenAllowTearingFalse_FlagsDoNotContainAllowTearing()
    {
        using var manager = D3D11DeviceManager.Create();
        var options = new DxgiPresenterOptions
        {
            AllowTearing = false,
        };
        using var presenter = new DxgiPresenter(manager, options);

        DxgiSwapChainDesc1 desc = presenter.GetDescription();
        Assert.Equal(0u, (uint)(desc.Flags & DxgiSwapChainFlags.AllowTearing));
        Assert.False(presenter.TearingEnabled);
    }

    [Fact]
    public void MaximumFrameLatency_DefaultsToOne_AndReflectsOptions()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenterDefault = new DxgiPresenter(manager);
        Assert.Equal(1u, presenterDefault.MaximumFrameLatency);

        var options = new DxgiPresenterOptions
        {
            MaximumFrameLatency = 3,
        };
        using var presenterCustom = new DxgiPresenter(manager, options);
        Assert.Equal(3u, presenterCustom.MaximumFrameLatency);
    }

    [Theory]
    [InlineData(0u, 1u)]
    [InlineData(1u, 1u)]
    [InlineData(2u, 0u)]
    [InlineData(2u, 17u)]
    public void Constructor_InvalidOptions_ThrowsArgumentOutOfRangeException(uint bufferCount, uint latency)
    {
        using var manager = D3D11DeviceManager.Create();
        var options = new DxgiPresenterOptions
        {
            BufferCount = bufferCount,
            MaximumFrameLatency = latency,
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new DxgiPresenter(manager, options));
    }

    [Fact]
    public void Constructor_NullDeviceManager_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DxgiPresenter(null!));
    }

    [Fact]
    public void FrameLatencyWaitHandle_WaitAndLifecycle()
    {
        using var manager = D3D11DeviceManager.Create();
        var presenter = new DxgiPresenter(manager);

        var handle = presenter.FrameLatencyWaitHandle;
        Assert.NotNull(handle);
        Assert.False(handle.IsInvalid);
        Assert.False(handle.IsClosed);

        // Fresh swap chain wait succeeds
        Assert.True(presenter.WaitForNextFrame(1000));

        // After Clear + Present loops, waits keep succeeding
        for (int i = 0; i < 10; i++)
        {
            presenter.Clear(0.1f * i, 0.2f, 0.3f, 1f);
            presenter.Present(vsync: true);
            Assert.True(presenter.WaitForNextFrame(1000));
        }

        presenter.Dispose();

        Assert.True(handle.IsClosed);
        Assert.Throws<ObjectDisposedException>(() => _ = presenter.SwapChainPointer);

        // Double dispose is safe
        presenter.Dispose();
    }

    [Fact]
    public void Present_ReturnsOccludedOrOk_WithoutDeviceLoss_ForVsyncTrueAndFalse()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        Assert.True(presenter.WaitForNextFrame(1000));
        presenter.Clear(0f, 0f, 0f, 1f);
        PresentResult resSync = presenter.Present(vsync: true);
        Assert.True(resSync is PresentResult.Ok or PresentResult.Occluded);
        Assert.False(presenter.IsDeviceLost);

        Assert.True(presenter.WaitForNextFrame(1000));
        presenter.Clear(1f, 1f, 1f, 1f);
        PresentResult resNoSync = presenter.Present(vsync: false);
        Assert.True(resNoSync is PresentResult.Ok or PresentResult.Occluded);
        Assert.False(presenter.IsDeviceLost);
    }

    [Fact]
    public void ZeroAllocation_DuringRenderLoopAndResizeChecks()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        // Warm up to eliminate any one-time JIT or static initialization allocations
        for (int i = 0; i < 5; i++)
        {
            presenter.WaitForNextFrame(1000);
            presenter.Clear(0f, 0f, 0f, 1f);
            presenter.Present(vsync: true);
            presenter.SetTargetSize(100, 100, 1.0f, 1.0f);
            presenter.ApplyPendingResize();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            presenter.WaitForNextFrame(1000);
            presenter.Clear(0.2f, 0.4f, 0.6f, 1.0f);
            presenter.Present(vsync: true);
            // Alternate sizes so ResizeBuffers and the back-buffer view rebuild are measured too.
            presenter.SetTargetSize(100 + (i & 1), 100, 1.0f, 1.0f);
            presenter.ApplyPendingResize();
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Resize_SetTargetSizeAndApplyPendingResize_UpdatesDimensionsAndMatrixTransform()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        DxgiSwapChainFlags originalFlags = presenter.SwapChainFlags;

        presenter.SetTargetSize(800, 450, 1.5f, 1.5f);
        bool resized = presenter.ApplyPendingResize();

        Assert.True(resized);
        Assert.Equal(1200u, presenter.PixelWidth);
        Assert.Equal(675u, presenter.PixelHeight);

        DxgiSwapChainDesc1 desc = presenter.GetDescription();
        Assert.Equal(1200u, desc.Width);
        Assert.Equal(675u, desc.Height);
        Assert.Equal(originalFlags, desc.Flags);

        float expectedScale = 1f / 1.5f;
        Assert.InRange(presenter.MatrixTransform.M11, expectedScale - 1e-4f, expectedScale + 1e-4f);
        Assert.InRange(presenter.MatrixTransform.M22, expectedScale - 1e-4f, expectedScale + 1e-4f);
        Assert.Equal(0f, presenter.MatrixTransform.M12);
        Assert.Equal(0f, presenter.MatrixTransform.M21);
        Assert.Equal(0f, presenter.MatrixTransform.M31);
        Assert.Equal(0f, presenter.MatrixTransform.M32);

        Assert.Equal(1.5f, presenter.CompositionScaleX);
        Assert.Equal(1.5f, presenter.CompositionScaleY);

        // Calling ApplyPendingResize again without changes returns false
        Assert.False(presenter.ApplyPendingResize());

        // Setting same size again returns false on ApplyPendingResize
        presenter.SetTargetSize(800, 450, 1.5f, 1.5f);
        Assert.False(presenter.ApplyPendingResize());
    }

    [Theory]
    [InlineData(0, 0, 1f, 1f, 1u, 1u)]
    [InlineData(-10, -5, 1f, 1f, 1u, 1u)]
    [InlineData(double.NaN, double.NaN, 1f, 1f, 1u, 1u)]
    [InlineData(100, 100, float.NaN, float.NaN, 100u, 100u)]
    [InlineData(100, 100, -1f, 0f, 100u, 100u)]
    [InlineData(100, 100, float.PositiveInfinity, float.NegativeInfinity, 100u, 100u)]
    [InlineData(30000, 20000, 1f, 1f, 16384u, 16384u)]
    [InlineData(10000, 10000, 2f, 2f, 16384u, 16384u)]
    [InlineData(1.4, 1.6, 1f, 1f, 1u, 2u)]
    [InlineData(1.5, 2.5, 1f, 1f, 2u, 3u)]
    public void ComputeTargetSize_HandlesEdgeCasesAndClamping(
        double logicalW, double logicalH, float scaleX, float scaleY, uint expectedW, uint expectedH)
    {
        SwapChainTargetSize size = DxgiPresenter.ComputeTargetSize(logicalW, logicalH, scaleX, scaleY);
        Assert.Equal(expectedW, size.Width);
        Assert.Equal(expectedH, size.Height);
    }

    [Fact]
    public void DisposalOrder_PresenterKeepsDeviceAliveUntilDisposed()
    {
        var manager = D3D11DeviceManager.Create();
        nint devicePtr = manager.DevicePointer;
        Marshal.AddRef(devicePtr);

        var presenter = new DxgiPresenter(manager);

        // Dispose manager first
        manager.Dispose();
        Assert.True(manager.IsDisposed);

        // Presenter still functions and holds device alive
        Assert.False(presenter.IsDeviceLost);
        Assert.True(presenter.WaitForNextFrame(1000));
        presenter.Clear(0f, 0f, 0f, 1f);
        PresentResult result = presenter.Present(vsync: true);
        Assert.True(result is PresentResult.Ok or PresentResult.Occluded);

        // Dispose presenter
        presenter.Dispose();

        int remaining = Marshal.Release(devicePtr);
        Assert.Equal(0, remaining);
    }

    [Fact]
    public void SwapChainPanelNative_SetSwapChain_NullPanel_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => SwapChainPanelNative.SetSwapChain(0, 0));
        Assert.Throws<ArgumentNullException>(() => SwapChainPanelNative.SetSwapChain(0, 12345));
    }
}
