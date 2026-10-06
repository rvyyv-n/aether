using System;
using Aether.Core.Interop.DirectX.Native;
using Microsoft.Win32.SafeHandles;

namespace Aether.Core.Interop.DirectX;

public sealed class DxgiPresenterOptions
{
    /// <summary>Initial back-buffer width in physical pixels.</summary>
    public uint Width { get; init; } = 1;

    /// <summary>Initial back-buffer height in physical pixels.</summary>
    public uint Height { get; init; } = 1;

    /// <summary>Flip model needs at least two buffers.</summary>
    public uint BufferCount { get; init; } = 2;

    public DxgiFormat Format { get; init; } = DxgiFormat.B8G8R8A8Unorm;

    /// <summary>
    /// Ignore keeps the swap chain opaque, which lets DWM use independent flip / overlays.
    /// Use Premultiplied only if XAML content behind the panel must show through.
    /// </summary>
    public DxgiAlphaMode AlphaMode { get; init; } = DxgiAlphaMode.Ignore;

    /// <summary>Adds <c>DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING</c> when the adapter supports it.</summary>
    public bool AllowTearing { get; init; } = true;

    /// <summary>Frames the CPU may queue ahead of the display. 1 gives the lowest latency.</summary>
    public uint MaximumFrameLatency { get; init; } = 1;
}

/// <summary>
/// Composition flip-discard swap chain for a XAML <c>SwapChainPanel</c>, with a
/// frame-latency waitable object.
/// <para>
/// Threading: <see cref="WaitForNextFrame"/>, <see cref="Clear"/> and <see cref="Present"/>
/// belong to one render thread (the owner). They don't allocate.
/// </para>
/// </summary>
public sealed unsafe class DxgiPresenter : IDisposable
{
    private nint _device;
    private nint _context;
    private nint _swapChain; // IDXGISwapChain2*
    private nint _rtv;       // cached ID3D11RenderTargetView* for buffer 0
    private readonly SafeWaitHandle _frameLatencyHandle;

    public DxgiPresenter(D3D11DeviceManager deviceManager, DxgiPresenterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(deviceManager);
        options ??= new DxgiPresenterOptions();
        if (options.BufferCount < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Flip-model swap chains need at least two buffers.");
        }
        if (options.MaximumFrameLatency is < 1 or > 16)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaximumFrameLatency must be 1-16.");
        }

        var flags = DxgiSwapChainFlags.FrameLatencyWaitableObject;
        if (options.AllowTearing && deviceManager.SupportsTearing)
        {
            flags |= DxgiSwapChainFlags.AllowTearing;
        }

        var desc = new DxgiSwapChainDesc1
        {
            Width = Math.Max(1, options.Width),
            Height = Math.Max(1, options.Height),
            Format = options.Format,
            SampleCount = 1,
            BufferUsage = DxNative.DXGI_USAGE_RENDER_TARGET_OUTPUT,
            BufferCount = options.BufferCount,
            Scaling = DxgiScaling.Stretch, // the only scaling mode composition swap chains accept
            SwapEffect = DxgiSwapEffect.FlipDiscard,
            AlphaMode = options.AlphaMode,
            Flags = flags,
        };

        nint device = deviceManager.DevicePointer;
        nint factory = deviceManager.DxgiFactoryPointer;
        nint swapChain1 = 0;
        try
        {
            nint sc1 = 0;
            // IDXGIFactory2::CreateSwapChainForComposition
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, nint, DxgiSwapChainDesc1*, nint, nint*, int>)Com.Vtbl(factory)[24])(
                factory, device, &desc, 0, &sc1));
            swapChain1 = sc1;
            Com.ThrowIfFailed(Com.QueryInterface(swapChain1, DxNative.IID_IDXGISwapChain2, out _swapChain));

            // IDXGISwapChain2::SetMaximumFrameLatency
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, uint, int>)Com.Vtbl(_swapChain)[31])(_swapChain, options.MaximumFrameLatency));

            // IDXGISwapChain2::GetFrameLatencyWaitableObject; we own the returned handle.
            nint waitable = ((delegate* unmanaged[Stdcall]<nint, nint>)Com.Vtbl(_swapChain)[33])(_swapChain);
            if (waitable == 0)
            {
                throw new InvalidOperationException("Swap chain returned no frame-latency waitable object.");
            }
            _frameLatencyHandle = new SafeWaitHandle(waitable, ownsHandle: true);
        }
        catch
        {
            Com.Release(ref _swapChain);
            Com.Release(ref swapChain1);
            throw;
        }
        Com.Release(ref swapChain1);

        // Keep the device alive for as long as this presenter, independent of disposal order.
        Com.AddRef(device);
        _device = device;
        nint context = deviceManager.ImmediateContextPointer;
        Com.AddRef(context);
        _context = context;

        SwapChainFlags = flags;
        PixelWidth = desc.Width;
        PixelHeight = desc.Height;
    }

    ~DxgiPresenter() => ReleaseAll();

    /// <summary>Borrowed <c>IDXGISwapChain2*</c> (also a valid <c>IDXGISwapChain1*</c>) for <c>ISwapChainPanelNative::SetSwapChain</c>.</summary>
    public nint SwapChainPointer => _swapChain != 0 ? _swapChain : throw new ObjectDisposedException(nameof(DxgiPresenter));

    /// <summary>Flags the swap chain was created with; every ResizeBuffers call passes the same set.</summary>
    public DxgiSwapChainFlags SwapChainFlags { get; }

    public bool TearingEnabled => (SwapChainFlags & DxgiSwapChainFlags.AllowTearing) != 0;

    public uint PixelWidth { get; private set; }

    public uint PixelHeight { get; private set; }

    /// <summary>Set once Present or ResizeBuffers reports device removal/reset. Recreate device and presenter.</summary>
    public bool IsDeviceLost { get; private set; }

    /// <summary>Frame-latency waitable object. Signalled when the swap chain can accept another frame.</summary>
    public SafeWaitHandle FrameLatencyWaitHandle => _frameLatencyHandle;

    /// <summary><c>IDXGISwapChain2::GetMaximumFrameLatency</c>.</summary>
    public uint MaximumFrameLatency
    {
        get
        {
            uint latency = 0;
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, uint*, int>)Com.Vtbl(SwapChainPointer)[32])(_swapChain, &latency));
            return latency;
        }
    }

    /// <summary><c>IDXGISwapChain1::GetDesc1</c>.</summary>
    public DxgiSwapChainDesc1 GetDescription()
    {
        DxgiSwapChainDesc1 desc;
        Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, DxgiSwapChainDesc1*, int>)Com.Vtbl(SwapChainPointer)[18])(_swapChain, &desc));
        return desc;
    }

    /// <summary>
    /// Blocks the render thread until the swap chain can take a new frame. Call once per
    /// frame before rendering. Returns false on timeout. Never call this on the UI thread.
    /// </summary>
    public bool WaitForNextFrame(uint timeoutMilliseconds = 1000)
    {
        uint result = DxNative.WaitForSingleObjectEx(_frameLatencyHandle, timeoutMilliseconds, 0);
        if (result == DxNative.WAIT_FAILED)
        {
            throw new System.ComponentModel.Win32Exception();
        }
        return result == DxNative.WAIT_OBJECT_0;
    }

    /// <summary>Clears the current back buffer.</summary>
    public void Clear(float r, float g, float b, float a)
    {
        nint rtv = EnsureRenderTargetView();
        float* color = stackalloc float[4] { r, g, b, a };
        // ID3D11DeviceContext::ClearRenderTargetView
        ((delegate* unmanaged[Stdcall]<nint, nint, float*, void>)Com.Vtbl(_context)[50])(_context, rtv, color);
    }

    /// <summary>
    /// Presents the back buffer. With <paramref name="vsync"/> false and tearing enabled the
    /// frame is presented immediately (<c>DXGI_PRESENT_ALLOW_TEARING</c>).
    /// </summary>
    public PresentResult Present(bool vsync = true)
    {
        nint sc = SwapChainPointer;
        uint flags = !vsync && TearingEnabled ? DxNative.DXGI_PRESENT_ALLOW_TEARING : 0;
        // IDXGISwapChain::Present
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Com.Vtbl(sc)[8])(sc, vsync ? 1u : 0u, flags);
        if (hr == DxNative.DXGI_STATUS_OCCLUDED)
        {
            return PresentResult.Occluded;
        }
        if (IsDeviceLostResult(hr))
        {
            IsDeviceLost = true;
            return PresentResult.DeviceLost;
        }
        Com.ThrowIfFailed(hr);
        return PresentResult.Ok;
    }

    private nint EnsureRenderTargetView()
    {
        nint rtv = _rtv;
        if (rtv != 0)
        {
            return rtv;
        }

        // In D3D11 flip model, buffer 0 always aliases the current back buffer, so one view suffices.
        nint sc = SwapChainPointer;
        Guid iid = DxNative.IID_ID3D11Texture2D;
        nint texture = 0;
        Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Com.Vtbl(sc)[9])(sc, 0, &iid, &texture));
        try
        {
            // ID3D11Device::CreateRenderTargetView
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, nint, void*, nint*, int>)Com.Vtbl(_device)[9])(_device, texture, null, &rtv));
        }
        finally
        {
            Com.Release(texture);
        }
        _rtv = rtv;
        return rtv;
    }

    private static bool IsDeviceLostResult(int hr) =>
        hr == DxNative.DXGI_ERROR_DEVICE_REMOVED || hr == DxNative.DXGI_ERROR_DEVICE_RESET;

    public void Dispose()
    {
        ReleaseAll();
        GC.SuppressFinalize(this);
    }

    private void ReleaseAll()
    {
        Com.Release(ref _rtv);
        _frameLatencyHandle?.Dispose();
        Com.Release(ref _swapChain);
        Com.Release(ref _context);
        Com.Release(ref _device);
    }
}
