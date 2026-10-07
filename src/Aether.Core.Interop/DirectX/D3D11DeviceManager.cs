using System;
using Aether.Core.Interop.DirectX.Native;

namespace Aether.Core.Interop.DirectX;

public sealed class D3D11DeviceOptions
{
    /// <summary>Driver to try first. Hardware falls back to WARP when <see cref="AllowWarpFallback"/> is set.</summary>
    public D3DDriverType DriverType { get; init; } = D3DDriverType.Hardware;

    public bool AllowWarpFallback { get; init; } = true;

    /// <summary>Lowest acceptable feature level. 11.1 is always requested first.</summary>
    public D3DFeatureLevel MinimumFeatureLevel { get; init; } = D3DFeatureLevel.Level11_0;

    /// <summary>Video support is needed for d3d11va decode. Ignored on WARP, which rejects it.</summary>
    public bool VideoSupport { get; init; } = true;

    /// <summary>Requests the SDK debug layer; silently dropped when the layer isn't installed.</summary>
    public bool DebugLayer { get; init; }
}

/// <summary>
/// Owns the Direct3D 11 device, its immediate context and the DXGI factory of the
/// device's adapter. The device is created with BGRA support (required for
/// composition), video support (d3d11va) and multithread protection, because the
/// immediate context is shared between the UI thread (resize) and the render thread.
/// </summary>
public sealed unsafe class D3D11DeviceManager : IDisposable
{
    private nint _device;
    private nint _context;
    private nint _factory;

    private D3D11DeviceManager(nint device, nint context, nint factory, D3DDriverType driverType,
        D3DFeatureLevel featureLevel, bool supportsTearing)
    {
        _device = device;
        _context = context;
        _factory = factory;
        DriverType = driverType;
        FeatureLevel = featureLevel;
        SupportsTearing = supportsTearing;
    }

    ~D3D11DeviceManager() => ReleaseAll();

    /// <summary>Borrowed <c>ID3D11Device*</c>. Valid until <see cref="Dispose"/>; AddRef to keep it longer.</summary>
    public nint DevicePointer => _device != 0 ? _device : throw new ObjectDisposedException(nameof(D3D11DeviceManager));

    /// <summary>Borrowed <c>ID3D11DeviceContext*</c> (immediate context).</summary>
    public nint ImmediateContextPointer => _context != 0 ? _context : throw new ObjectDisposedException(nameof(D3D11DeviceManager));

    /// <summary>Borrowed <c>IDXGIFactory2*</c> that created the device's adapter.</summary>
    public nint DxgiFactoryPointer => _factory != 0 ? _factory : throw new ObjectDisposedException(nameof(D3D11DeviceManager));

    public D3DDriverType DriverType { get; }

    public D3DFeatureLevel FeatureLevel { get; }

    /// <summary>True when the factory reports <c>DXGI_FEATURE_PRESENT_ALLOW_TEARING</c> (variable refresh).</summary>
    public bool SupportsTearing { get; }

    public bool IsDisposed => _device == 0;

    /// <summary><c>ID3D11Device::GetCreationFlags</c>.</summary>
    public D3D11CreateDeviceFlags CreationFlags =>
        (D3D11CreateDeviceFlags)((delegate* unmanaged[Stdcall]<nint, uint>)Com.Vtbl(DevicePointer)[38])(_device);

    /// <summary><c>ID3D11Device::GetDeviceRemovedReason</c>; S_OK while the device is healthy.</summary>
    public int DeviceRemovedReason =>
        ((delegate* unmanaged[Stdcall]<nint, int>)Com.Vtbl(DevicePointer)[39])(_device);

    /// <summary><c>ID3D11Multithread::GetMultithreadProtected</c>.</summary>
    public bool IsMultithreadProtected
    {
        get
        {
            Com.ThrowIfFailed(Com.QueryInterface(ImmediateContextPointer, DxNative.IID_ID3D11Multithread, out nint mt));
            try
            {
                return ((delegate* unmanaged[Stdcall]<nint, int>)Com.Vtbl(mt)[6])(mt) != 0;
            }
            finally
            {
                Com.Release(mt);
            }
        }
    }

    public static D3D11DeviceManager Create(D3D11DeviceOptions? options = null)
    {
        options ??= new D3D11DeviceOptions();

        var flags = D3D11CreateDeviceFlags.BgraSupport;
        if (options.VideoSupport)
        {
            flags |= D3D11CreateDeviceFlags.VideoSupport;
        }
        if (options.DebugLayer)
        {
            flags |= D3D11CreateDeviceFlags.Debug;
        }

        D3DDriverType driver = options.DriverType;
        if (driver == D3DDriverType.Warp)
        {
            flags &= ~D3D11CreateDeviceFlags.VideoSupport;
        }
        int hr = TryCreate(driver, flags, options.MinimumFeatureLevel, out nint device, out nint context, out D3DFeatureLevel level);
        if (hr == DxNative.DXGI_ERROR_SDK_COMPONENT_MISSING && (flags & D3D11CreateDeviceFlags.Debug) != 0)
        {
            flags &= ~D3D11CreateDeviceFlags.Debug;
            hr = TryCreate(driver, flags, options.MinimumFeatureLevel, out device, out context, out level);
        }
        if (hr < 0 && driver == D3DDriverType.Hardware && options.AllowWarpFallback)
        {
            // WARP rejects D3D11_CREATE_DEVICE_VIDEO_SUPPORT (DXGI_ERROR_UNSUPPORTED) and has no decoder anyway.
            driver = D3DDriverType.Warp;
            flags &= ~D3D11CreateDeviceFlags.VideoSupport;
            hr =TryCreate(driver, flags, options.MinimumFeatureLevel, out device, out context, out level);
        }
        Com.ThrowIfFailed(hr);

        nint factory = 0;
        try
        {
            EnableMultithreadProtection(context);
            factory = GetAdapterFactory(device);
            bool tearing = QueryTearingSupport(factory);
            return new D3D11DeviceManager(device, context, factory, driver, level, tearing);
        }
        catch
        {
            Com.Release(ref factory);
            Com.Release(ref context);
            Com.Release(ref device);
            throw;
        }
    }

    private static int TryCreate(D3DDriverType driver, D3D11CreateDeviceFlags flags, D3DFeatureLevel minimum,
        out nint device, out nint context, out D3DFeatureLevel level)
    {
        D3DFeatureLevel* levels = stackalloc D3DFeatureLevel[4];
        uint count = 0;
        foreach (D3DFeatureLevel l in (ReadOnlySpan<D3DFeatureLevel>)[D3DFeatureLevel.Level11_1, D3DFeatureLevel.Level11_0, D3DFeatureLevel.Level10_1, D3DFeatureLevel.Level10_0])
        {
            if (l >= minimum)
            {
                levels[count++] = l;
            }
        }

        nint d = 0, c = 0;
        D3DFeatureLevel got = 0;
        int hr = DxNative.D3D11CreateDevice(0, driver, 0, flags, levels, count, DxNative.D3D11_SDK_VERSION, &d, &got, &c);
        device = hr >= 0 ? d : 0;
        context = hr >= 0 ? c : 0;
        level = got;
        return hr;
    }

    private static void EnableMultithreadProtection(nint context)
    {
        Com.ThrowIfFailed(Com.QueryInterface(context, DxNative.IID_ID3D11Multithread, out nint mt));
        try
        {
            // ID3D11Multithread::SetMultithreadProtected returns the previous state, not an HRESULT.
            ((delegate* unmanaged[Stdcall]<nint, int, int>)Com.Vtbl(mt)[5])(mt, 1);
        }
        finally
        {
            Com.Release(mt);
        }
    }

    /// <summary>Device → IDXGIDevice → IDXGIAdapter → GetParent(IDXGIFactory2).</summary>
    private static nint GetAdapterFactory(nint device)
    {
        nint dxgiDevice = 0, adapter = 0;
        try
        {
            Com.ThrowIfFailed(Com.QueryInterface(device, DxNative.IID_IDXGIDevice, out dxgiDevice));
            nint a = 0;
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, nint*, int>)Com.Vtbl(dxgiDevice)[7])(dxgiDevice, &a));
            adapter = a;

            Guid iid = DxNative.IID_IDXGIFactory2;
            nint factory = 0;
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Com.Vtbl(adapter)[6])(adapter, &iid, &factory));
            return factory;
        }
        finally
        {
            Com.Release(ref adapter);
            Com.Release(ref dxgiDevice);
        }
    }

    private static bool QueryTearingSupport(nint factory)
    {
        if (Com.QueryInterface(factory, DxNative.IID_IDXGIFactory5, out nint factory5) < 0)
        {
            return false;
        }
        try
        {
            int allow = 0;
            int hr = ((delegate* unmanaged[Stdcall]<nint, int, void*, uint, int>)Com.Vtbl(factory5)[28])(
                factory5, DxNative.DXGI_FEATURE_PRESENT_ALLOW_TEARING, &allow, sizeof(int));
            return hr >= 0 && allow != 0;
        }
        finally
        {
            Com.Release(factory5);
        }
    }

    public void Dispose()
    {
        ReleaseAll();
        GC.SuppressFinalize(this);
    }

    private void ReleaseAll()
    {
        nint context = _context;
        if (context != 0)
        {
            // ClearState + Flush drops pipeline bindings so deferred destruction runs now.
            ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(context)[110])(context);
            ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(context)[111])(context);
        }
        Com.Release(ref _factory);
        Com.Release(ref _context);
        Com.Release(ref _device);
    }
}
