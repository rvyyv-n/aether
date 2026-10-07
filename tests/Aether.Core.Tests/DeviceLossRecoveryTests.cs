using System;
using System.Threading.Tasks;
using Aether.Core.Interop.DirectX;
using Xunit;

namespace Aether.Core.Tests;

public class DeviceLossRecoveryTests
{
    private const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    private const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);

    [Fact]
    public void HealthyPresenter_HasDeviceLostFalse_AndReasonZero()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        Assert.False(presenter.IsDeviceLost);
        Assert.Equal(0, presenter.DeviceLostReason);
    }

    [Fact]
    public void ReportDeviceLost_WithDeviceReset_LatchesLossAndReasonOnHealthyDevice()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);

        Assert.True(presenter.IsDeviceLost);
        Assert.Equal(DXGI_ERROR_DEVICE_RESET, presenter.DeviceLostReason);
    }

    [Fact]
    public void ReportDeviceLost_SecondCallWithDifferentCode_DoesNotChangeReason()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);
        presenter.ReportDeviceLost(unchecked((int)0x887A0020));

        Assert.True(presenter.IsDeviceLost);
        Assert.Equal(DXGI_ERROR_DEVICE_RESET, presenter.DeviceLostReason);
    }

    [Fact]
    public void ReportDeviceLost_WithZero_RecordsDeviceRemoved()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        presenter.ReportDeviceLost(0);

        Assert.True(presenter.IsDeviceLost);
        Assert.Equal(DXGI_ERROR_DEVICE_REMOVED, presenter.DeviceLostReason);
    }

    [Fact]
    public void Present_AfterReportDeviceLost_ReturnsDeviceLost_ForVsyncTrueAndFalse()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_REMOVED);

        PresentResult resSync = presenter.Present(vsync: true);
        Assert.Equal(PresentResult.DeviceLost, resSync);

        PresentResult resNoSync = presenter.Present(vsync: false);
        Assert.Equal(PresentResult.DeviceLost, resNoSync);
    }

    [Fact]
    public void ApplyPendingResize_AfterReportDeviceLost_ReturnsFalse_AndPreservesDimensions()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        uint initialWidth = presenter.PixelWidth;
        uint initialHeight = presenter.PixelHeight;

        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_REMOVED);
        presenter.SetTargetSize(initialWidth + 200, initialHeight + 200, 1.0f, 1.0f);

        bool resized = presenter.ApplyPendingResize();

        Assert.False(resized);
        Assert.Equal(initialWidth, presenter.PixelWidth);
        Assert.Equal(initialHeight, presenter.PixelHeight);
    }

    [Fact]
    public void ZeroAllocation_PresentAndIsDeviceLost_OnHealthyPath()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        for (int i = 0; i < 5; i++)
        {
            presenter.WaitForNextFrame(1000);
            presenter.Clear(0f, 0f, 0f, 1f);
            _ = presenter.Present(vsync: true);
            _ = presenter.IsDeviceLost;
            _ = presenter.DeviceLostReason;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            presenter.WaitForNextFrame(1000);
            presenter.Clear(0.1f, 0.2f, 0.3f, 1f);
            _ = presenter.Present(vsync: true);
            _ = presenter.IsDeviceLost;
            _ = presenter.DeviceLostReason;
        }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void ZeroAllocation_ReportDeviceLost_AndSubsequentChecks()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        using (var warmupPresenter = new DxgiPresenter(manager))
        {
            warmupPresenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);
            _ = warmupPresenter.IsDeviceLost;
            _ = warmupPresenter.DeviceLostReason;
            _ = warmupPresenter.Present(vsync: true);
        }

        long beforeFirst = GC.GetAllocatedBytesForCurrentThread();
        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);
        _ = presenter.IsDeviceLost;
        _ = presenter.DeviceLostReason;
        _ = presenter.Present(vsync: true);
        long afterFirst = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, afterFirst - beforeFirst);

        long beforeLoop = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);
            _ = presenter.IsDeviceLost;
            _ = presenter.DeviceLostReason;
            _ = presenter.Present(vsync: true);
        }
        long afterLoop = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, afterLoop - beforeLoop);
    }

    [Fact]
    public async Task IsDeviceLost_ReadableFromAnotherThread_AfterReport()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        presenter.ReportDeviceLost(DXGI_ERROR_DEVICE_RESET);

        bool isLost = await Task.Run(() => presenter.IsDeviceLost);
        int reason = await Task.Run(() => presenter.DeviceLostReason);

        Assert.True(isLost);
        Assert.Equal(DXGI_ERROR_DEVICE_RESET, reason);
    }

    [Fact]
    public unsafe void GetBackBufferTexture_ReturnsNonZero_RefcountBalanceAllowsDisposal()
    {
        using var manager = D3D11DeviceManager.Create();
        using var presenter = new DxgiPresenter(manager);

        nint texture = presenter.GetBackBufferTexture();
        Assert.NotEqual(0, texture);

        ReleaseCom(texture);

        Assert.True(presenter.WaitForNextFrame(1000));
        presenter.Clear(0.1f, 0.2f, 0.3f, 1f);
        PresentResult res = presenter.Present(vsync: true);
        Assert.True(res is PresentResult.Ok or PresentResult.Occluded);
    }

    [Fact]
    public void ClearStateOnResizeFalse_ResizeStillWorksWithD3DPath()
    {
        using var manager = D3D11DeviceManager.Create();
        var options = new DxgiPresenterOptions
        {
            ClearStateOnResize = false,
        };
        using var presenter = new DxgiPresenter(manager, options);

        presenter.SetTargetSize(640, 360, 1.0f, 1.0f);
        bool resized = presenter.ApplyPendingResize();

        Assert.True(resized);
        Assert.Equal(640u, presenter.PixelWidth);
        Assert.Equal(360u, presenter.PixelHeight);
        Assert.False(presenter.IsDeviceLost);
    }

    private static unsafe uint ReleaseCom(nint ptr) =>
        ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)ptr)[2])(ptr);
}
