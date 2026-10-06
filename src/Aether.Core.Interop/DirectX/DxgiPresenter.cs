using System;
using System.Numerics;
using System.Threading;
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
/// Threading: <see cref="WaitForNextFrame"/>, <see cref="Clear"/>, <see cref="Present"/>
/// and <see cref="ApplyPendingResize"/> belong to one render thread (the owner). They don't
/// allocate. <see cref="SetTargetSize"/> may be called from any thread (normally the UI
/// thread's SizeChanged / CompositionScaleChanged) and never blocks on the GPU.
/// </para>
/// </summary>
public sealed unsafe class DxgiPresenter : IDisposable
{
    private nint _device;
    private nint _context;
    private nint _swapChain; // IDXGISwapChain2*
    private nint _rtv;       // cached ID3D11RenderTargetView* for buffer 0
    private readonly SafeWaitHandle _frameLatencyHandle;

    /// <summary>Largest D3D11 texture dimension (D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION).</summary>
    public const uint MaxDimension = 16384;

    private readonly Lock _pendingLock = new();
    private volatile bool _resizePending;
    private SwapChainTargetSize _pendingSize;
    private float _pendingScaleX = 1f;
    private float _pendingScaleY = 1f;

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

    /// <summary>Composition scale currently compensated by the swap chain's matrix transform.</summary>
    public float CompositionScaleX { get; private set; } = 1f;

    public float CompositionScaleY { get; private set; } = 1f;

    /// <summary><c>IDXGISwapChain2::GetMatrixTransform</c>.</summary>
    public Matrix3x2 MatrixTransform
    {
        get
        {
            Matrix3x2 m;
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, Matrix3x2*, int>)Com.Vtbl(SwapChainPointer)[35])(_swapChain, &m));
            return m;
        }
    }

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

    /// <summary>
    /// Physical back-buffer size for a panel of <paramref name="logicalWidth"/> x
    /// <paramref name="logicalHeight"/> DIPs at the given composition scale, clamped to 1..16384.
    /// </summary>
    public static SwapChainTargetSize ComputeTargetSize(double logicalWidth, double logicalHeight, float scaleX, float scaleY) =>
        new(ToPixels(logicalWidth, scaleX), ToPixels(logicalHeight, scaleY));

    private static uint ToPixels(double logical, float scale)
    {
        double px = logical * SanitizeScale(scale);
        if (!(px >= 1)) // also catches NaN
        {
            return 1;
        }
        return px >= MaxDimension ? MaxDimension : (uint)Math.Round(px, MidpointRounding.AwayFromZero);
    }

    private static float SanitizeScale(float scale) => float.IsFinite(scale) && scale > 0 ? scale : 1f;

    /// <summary>
    /// Records the panel's size (DIPs) and composition scale (<c>SwapChainPanel.CompositionScaleX/Y</c>).
    /// Any thread; cheap; the swap chain changes on the next <see cref="ApplyPendingResize"/>.
    /// </summary>
    public void SetTargetSize(double logicalWidth, double logicalHeight, float compositionScaleX, float compositionScaleY)
    {
        SwapChainTargetSize size = ComputeTargetSize(logicalWidth, logicalHeight, compositionScaleX, compositionScaleY);
        lock (_pendingLock)
        {
            _pendingSize = size;
            _pendingScaleX = SanitizeScale(compositionScaleX);
            _pendingScaleY = SanitizeScale(compositionScaleY);
            _resizePending = true;
        }
    }

    /// <summary>
    /// Owner thread, between frames: applies the latest <see cref="SetTargetSize"/>.
    /// Resizes the buffers (<c>ResizeBuffers</c>) when the pixel size changed and sets the
    /// inverse composition-scale matrix (<c>SetMatrixTransform</c>) when the scale changed,
    /// so the pixel-sized buffer maps 1:1 onto physical pixels instead of being scaled twice.
    /// Returns true if anything changed; false with <see cref="IsDeviceLost"/> set on device loss.
    /// </summary>
    public bool ApplyPendingResize()
    {
        if (!_resizePending)
        {
            return false;
        }

        SwapChainTargetSize size;
        float scaleX, scaleY;
        lock (_pendingLock)
        {
            size = _pendingSize;
            scaleX = _pendingScaleX;
            scaleY = _pendingScaleY;
            _resizePending = false;
        }

        nint sc = SwapChainPointer;
        bool changed = false;

        if (size.Width != PixelWidth || size.Height != PixelHeight)
        {
            // ResizeBuffers fails while anything still references a back buffer.
            Com.Release(ref _rtv);
            ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(_context)[110])(_context); // ClearState
            ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(_context)[111])(_context); // Flush

            // IDXGISwapChain::ResizeBuffers: keep count and format; flags must match creation.
            int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, DxgiFormat, uint, int>)Com.Vtbl(sc)[13])(
                sc, 0, size.Width, size.Height, DxgiFormat.Unknown, (uint)SwapChainFlags);
            if (IsDeviceLostResult(hr))
            {
                IsDeviceLost = true;
                return false;
            }
            Com.ThrowIfFailed(hr);
            PixelWidth = size.Width;
            PixelHeight = size.Height;
            changed = true;
        }

        if (scaleX != CompositionScaleX || scaleY != CompositionScaleY)
        {
            var inverse = new Matrix3x2(1f / scaleX, 0f, 0f, 1f / scaleY, 0f, 0f);
            // IDXGISwapChain2::SetMatrixTransform
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, Matrix3x2*, int>)Com.Vtbl(sc)[34])(sc, &inverse));
            CompositionScaleX = scaleX;
            CompositionScaleY = scaleY;
            changed = true;
        }

        return changed;
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
