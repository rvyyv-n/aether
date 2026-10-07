using System;
using System.Runtime.InteropServices;
using Aether.Core.Interop.DirectX;
using Xunit;

namespace Aether.Core.Tests;

public class D3D11DeviceManagerTests
{
    [Fact]
    public void Create_DefaultOptions_SucceedsAndHasExpectedProperties()
    {
        using var manager = D3D11DeviceManager.Create();

        Assert.False(manager.IsDisposed);
        Assert.NotEqual(0, manager.DevicePointer);
        Assert.NotEqual(0, manager.ImmediateContextPointer);
        Assert.NotEqual(0, manager.DxgiFactoryPointer);
        Assert.True(manager.FeatureLevel >= D3DFeatureLevel.Level11_0);
        Assert.True((manager.CreationFlags & D3D11CreateDeviceFlags.BgraSupport) != 0);

        if (manager.DriverType == D3DDriverType.Hardware)
        {
            Assert.True((manager.CreationFlags & D3D11CreateDeviceFlags.VideoSupport) != 0);
        }

        Assert.True(manager.IsMultithreadProtected);
        Assert.Equal(0, manager.DeviceRemovedReason);
    }

    [Fact]
    public void Create_ExplicitWarp_SucceedsAndHasNoVideoSupport()
    {
        var options = new D3D11DeviceOptions
        {
            DriverType = D3DDriverType.Warp,
        };

        using var manager = D3D11DeviceManager.Create(options);

        Assert.Equal(D3DDriverType.Warp, manager.DriverType);
        Assert.Equal(0u, (uint)(manager.CreationFlags & D3D11CreateDeviceFlags.VideoSupport));
        Assert.True((manager.CreationFlags & D3D11CreateDeviceFlags.BgraSupport) != 0);
        Assert.True(manager.FeatureLevel >= D3DFeatureLevel.Level11_0);
        Assert.True(manager.IsMultithreadProtected);
        Assert.Equal(0, manager.DeviceRemovedReason);
    }

    [Fact]
    public void Create_DebugLayerTrue_SucceedsWithOrWithoutSdkLayer()
    {
        var options = new D3D11DeviceOptions
        {
            DebugLayer = true,
        };

        using var manager = D3D11DeviceManager.Create(options);

        Assert.False(manager.IsDisposed);
        Assert.NotEqual(0, manager.DevicePointer);
        Assert.Equal(0, manager.DeviceRemovedReason);
    }

    [Fact]
    public void Create_MinimumFeatureLevel_IsRespected()
    {
        var options = new D3D11DeviceOptions
        {
            // WARP fallback always provides 11.1, so this holds on GPU-less CI too.
            MinimumFeatureLevel = D3DFeatureLevel.Level11_1,
        };

        using var manager = D3D11DeviceManager.Create(options);

        Assert.Equal(D3DFeatureLevel.Level11_1, manager.FeatureLevel);
    }

    [Fact]
    public void Dispose_SetsIsDisposed_PointersThrowObjectDisposed_AndDoubleDisposeIsSafe()
    {
        var manager = D3D11DeviceManager.Create();
        Assert.False(manager.IsDisposed);

        manager.Dispose();

        Assert.True(manager.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => _ = manager.DevicePointer);
        Assert.Throws<ObjectDisposedException>(() => _ = manager.ImmediateContextPointer);
        Assert.Throws<ObjectDisposedException>(() => _ = manager.DxgiFactoryPointer);

        manager.Dispose();
        Assert.True(manager.IsDisposed);
    }

    [Fact]
    public void Dispose_ReleasesDeviceReference()
    {
        var manager = D3D11DeviceManager.Create();
        nint devicePtr = manager.DevicePointer;

        Marshal.AddRef(devicePtr);
        manager.Dispose();

        int remaining = Marshal.Release(devicePtr);
        Assert.Equal(0, remaining);
    }
}
