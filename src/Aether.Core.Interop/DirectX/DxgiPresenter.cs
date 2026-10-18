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

    /// <summary>
    /// Run <c>ClearState</c> before <c>ResizeBuffers</c> so no pipeline binding still references a back
    /// buffer. Turn off when another API shares the immediate context and caches its state (ANGLE):
    /// it must release its own references (destroy its surface) before the resize instead.
    /// </summary>
    public bool ClearStateOnResize { get; init; } = true;
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
    private readonly bool _clearStateOnResize;

    /// <summary>Largest D3D11 texture dimension (D3D11_REQ_TEXTURE2D_U_OR_V_DIMENSION).</summary>
    public const uint MaxDimension = 16384;

    private readonly Lock _pendingLock = new();
    // Guards the swap chain's matrix transform and its lifetime against the UI thread's StretchToTarget.
    private readonly Lock _matrixLock = new();
    private bool _stretched;
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
        _clearStateOnResize = options.ClearStateOnResize;
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

    private int _deviceLostReason; // 0 while healthy, else the HRESULT that reported the loss

    /// <summary>
    /// Latched once Present or ResizeBuffers reports device removal/reset, or someone calls
    /// <see cref="ReportDeviceLost"/>. The presenter is dead from then on: the owner must recreate
    /// device, presenter and anything rendering into them. Readable from any thread; no allocation.
    /// </summary>
    public bool IsDeviceLost => Volatile.Read(ref _deviceLostReason) != 0;

    /// <summary>
    /// Why the device was lost: <c>ID3D11Device::GetDeviceRemovedReason</c> when it gives one,
    /// otherwise the HRESULT that reported it. 0 while healthy.
    /// </summary>
    public int DeviceLostReason => Volatile.Read(ref _deviceLostReason);

    /// <summary>
    /// Marks the device lost on behalf of another API sharing it (e.g. ANGLE reporting a context
    /// reset). The first report wins; later ones are ignored. No allocation. Any thread, but not
    /// concurrently with <see cref="Dispose"/>.
    /// </summary>
    /// <param name="hresult">The failure seen by the caller; a non-negative value means <c>DXGI_ERROR_DEVICE_REMOVED</c>.</param>
    public void ReportDeviceLost(int hresult)
    {
        int reason = hresult < 0 ? hresult : DxNative.DXGI_ERROR_DEVICE_REMOVED;
        nint device = _device;
        if (device != 0)
        {
            // ID3D11Device::GetDeviceRemovedReason: the driver's own reason (hung, reset, driver update).
            int removed = ((delegate* unmanaged[Stdcall]<nint, int>)Com.Vtbl(device)[39])(device);
            if (removed < 0)
            {
                reason = removed;
            }
        }
        Interlocked.CompareExchange(ref _deviceLostReason, reason, 0);
    }

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
        if (IsDeviceLost)
        {
            return PresentResult.DeviceLost;
        }
        uint flags = !vsync && TearingEnabled ? DxNative.DXGI_PRESENT_ALLOW_TEARING : 0;
        // IDXGISwapChain::Present
        int hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Com.Vtbl(sc)[8])(sc, vsync ? 1u : 0u, flags);
        if (hr == DxNative.DXGI_STATUS_OCCLUDED)
        {
            return PresentResult.Occluded;
        }
        if (IsDeviceLostResult(hr))
        {
            ReportDeviceLost(hr);
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
    /// Any thread: stretches the picture already on screen to the new panel size through the swap
    /// chain's matrix transform, so a resize shows a slightly soft picture instead of a void while the
    /// render thread catches up. Skipped if the render thread is busy resizing. Undone by
    /// <see cref="EndStretch"/> once the resized buffers have been presented.
    /// </summary>
    public void StretchToTarget(double logicalWidth, double logicalHeight, float compositionScaleX, float compositionScaleY)
    {
        SwapChainTargetSize size = ComputeTargetSize(logicalWidth, logicalHeight, compositionScaleX, compositionScaleY);
        float scaleX = SanitizeScale(compositionScaleX);
        float scaleY = SanitizeScale(compositionScaleY);
        if (!_matrixLock.TryEnter())
        {
            return;
        }
        try
        {
            nint sc = _swapChain;
            uint width = PixelWidth, height = PixelHeight;
            if (sc == 0 || width == 0 || height == 0 || IsDeviceLost)
            {
                return;
            }
            if (size.Width == width && size.Height == height)
            {
                if (_stretched)
                {
                    SetMatrix(sc, 1f / scaleX, 1f / scaleY);
                    _stretched = false;
                }
                return;
            }
            SetMatrix(sc, size.Width / (float)width / scaleX, size.Height / (float)height / scaleY);
            _stretched = true;
        }
        finally
        {
            _matrixLock.Exit();
        }
    }

    /// <summary>Owner thread, after presenting at the final size: back to the 1:1 mapping.</summary>
    public void EndStretch()
    {
        lock (_matrixLock)
        {
            if (!_stretched || _swapChain == 0)
            {
                return;
            }
            SetMatrix(_swapChain, 1f / CompositionScaleX, 1f / CompositionScaleY);
            _stretched = false;
        }
    }

    private static void SetMatrix(nint swapChain, float scaleX, float scaleY)
    {
        var matrix = new Matrix3x2(scaleX, 0f, 0f, scaleY, 0f, 0f);
        // IDXGISwapChain2::SetMatrixTransform
        Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, Matrix3x2*, int>)Com.Vtbl(swapChain)[34])(swapChain, &matrix));
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
        if (!_resizePending || IsDeviceLost)
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
            if (_clearStateOnResize)
            {
                ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(_context)[110])(_context); // ClearState
            }
            ((delegate* unmanaged[Stdcall]<nint, void>)Com.Vtbl(_context)[111])(_context); // Flush: run deferred releases now

            int hr;
            lock (_matrixLock)
            {
                // IDXGISwapChain::ResizeBuffers: keep count and format; flags must match creation.
                hr = ((delegate* unmanaged[Stdcall]<nint, uint, uint, uint, DxgiFormat, uint, int>)Com.Vtbl(sc)[13])(
                    sc, 0, size.Width, size.Height, DxgiFormat.Unknown, (uint)SwapChainFlags);
            }
            if (IsDeviceLostResult(hr))
            {
                ReportDeviceLost(hr);
                return false;
            }
            Com.ThrowIfFailed(hr);
            PixelWidth = size.Width;
            PixelHeight = size.Height;
            changed = true;
        }

        if (scaleX != CompositionScaleX || scaleY != CompositionScaleY)
        {
            lock (_matrixLock)
            {
                // While stretched, EndStretch writes the 1:1 matrix with the new scale.
                if (!_stretched)
                {
                    SetMatrix(sc, 1f / scaleX, 1f / scaleY);
                }
                CompositionScaleX = scaleX;
                CompositionScaleY = scaleY;
            }
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// <c>ID3D11Texture2D*</c> for buffer 0, with a reference the caller must release. In the D3D11
    /// flip model buffer 0 always aliases the current back buffer, so one wrapper (an ANGLE pbuffer,
    /// a view) stays valid across presents; release it before <see cref="ApplyPendingResize"/>.
    /// </summary>
    public nint GetBackBufferTexture()
    {
        nint sc = SwapChainPointer;
        Guid iid = DxNative.IID_ID3D11Texture2D;
        nint texture = 0;
        // IDXGISwapChain::GetBuffer
        Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, uint, Guid*, nint*, int>)Com.Vtbl(sc)[9])(sc, 0, &iid, &texture));
        return texture;
    }

    private nint EnsureRenderTargetView()
    {
        nint rtv = _rtv;
        if (rtv != 0)
        {
            return rtv;
        }

        // In D3D11 flip model, buffer 0 always aliases the current back buffer, so one view suffices.
        nint texture = GetBackBufferTexture();
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
        hr is DxNative.DXGI_ERROR_DEVICE_REMOVED or DxNative.DXGI_ERROR_DEVICE_RESET
            or DxNative.DXGI_ERROR_DEVICE_HUNG or DxNative.DXGI_ERROR_DRIVER_INTERNAL_ERROR;

    public void Dispose()
    {
        ReleaseAll();
        GC.SuppressFinalize(this);
    }

    private void ReleaseAll()
    {
        Com.Release(ref _rtv);
        _frameLatencyHandle?.Dispose();
        lock (_matrixLock)
        {
            Com.Release(ref _swapChain);
        }
        Com.Release(ref _context);
        Com.Release(ref _device);
    }
}
