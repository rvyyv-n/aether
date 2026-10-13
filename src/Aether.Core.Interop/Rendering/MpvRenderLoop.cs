using System;
using System.Threading;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.DirectX.Native;
using Aether.Core.Interop.Native;
using Microsoft.Win32.SafeHandles;

namespace Aether.Core.Interop.Rendering;

public sealed class MpvRenderLoopOptions
{
    public D3D11DeviceOptions Device { get; init; } = new();

    /// <summary>Keeps the loop redrawing (without new video frames) while its fade runs. Optional.</summary>
    public BackdropSource? Backdrop { get; init; }

    /// <summary>Swap chain template. Width/Height come from <see cref="MpvRenderLoop.SetTargetSize"/>; ClearStateOnResize from the backend.</summary>
    public DxgiPresenterOptions Presenter { get; init; } = new();

    /// <summary>Present with vsync. Off presents immediately (tearing, when supported).</summary>
    public bool VSync { get; init; } = true;

    /// <summary>Upper bound for one wait (frame-latency object, idle wake). Bounds how long a stop request can go unnoticed.</summary>
    public uint WaitTimeoutMilliseconds { get; init; } = 100;

    /// <summary>First delay before retrying a failed (re)creation; doubles up to <see cref="MaxRetryDelayMilliseconds"/>.</summary>
    public uint RetryDelayMilliseconds { get; init; } = 100;

    public uint MaxRetryDelayMilliseconds { get; init; } = 2000;

    /// <summary>Creates each device generation. Defaults to <see cref="D3D11DeviceManager.Create"/> with <see cref="Device"/>.</summary>
    public Func<D3D11DeviceOptions, D3D11DeviceManager>? DeviceFactory { get; init; }
}

public enum RenderLoopState
{
    NotStarted,
    Running,
    /// <summary>No device stack: waiting to (re)create after a loss or a failure.</summary>
    Recovering,
    Stopped,
}

/// <summary>
/// The dedicated render thread ("Aether.Render"). It owns the whole device stack, device →
/// presenter → backend (ANGLE + mpv render context), creates it on the thread that will use it
/// (EGL contexts are per thread), and runs <c>wait → Render → Present → ReportSwap</c>.
/// <para>
/// Hot path: no managed allocation. Wakes come from two kernel events, one signalled by mpv's
/// update callback through native code only, the other by <see cref="SetTargetSize"/>,
/// <see cref="RequestRedraw"/> and <see cref="Stop"/>.
/// </para>
/// <para>
/// Device loss: <see cref="DxgiPresenter.IsDeviceLost"/> is latched by Present/ResizeBuffers, or by
/// the backend reporting a lost GPU context. The loop then notifies the host, tears the stack down in
/// reverse order and builds a new one; the new swap chain reaches the UI through
/// <see cref="IRenderLoopHost.OnSwapChainCreated"/>. Failed creation retries with backoff.
/// </para>
/// </summary>
public sealed unsafe class MpvRenderLoop : IDisposable
{
    private readonly IVideoRenderBackendFactory _backendFactory;
    private readonly IRenderLoopHost _host;
    private readonly MpvRenderLoopOptions _options;
    private readonly Thread _thread;

    // Kernel events. The raw values are used on the render thread and in mpv's callback, so the
    // handles close only once both Dispose has been called and the thread has exited (two votes).
    private readonly SafeWaitHandle _frameEvent;
    private readonly SafeWaitHandle _wakeEvent;
    private readonly nint _frameEventRaw;
    private readonly nint _wakeEventRaw;

    private readonly Lock _sizeLock = new();
    private double _logicalWidth = 1, _logicalHeight = 1;
    private float _scaleX = 1f, _scaleY = 1f;
    private bool _sizePending;

    private volatile bool _stop;
    private int _redraw;
    private volatile RenderLoopState _state;
    private int _started;
    private int _disposed;
    private int _closeVotes;
    private int _generation;
    private int _deviceLossCount;
    private long _framesPresented;

    // Render-thread state.
    private D3D11DeviceManager? _device;
    private DxgiPresenter? _presenter;
    private IVideoRenderBackend? _backend;

    /// <param name="backendFactory">Video backend; null clears to black through D3D11 only.</param>
    public MpvRenderLoop(IVideoRenderBackendFactory? backendFactory, IRenderLoopHost host, MpvRenderLoopOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        _backendFactory = backendFactory ?? new ClearRenderBackendFactory();
        _host = host;
        _options = options ?? new MpvRenderLoopOptions();

        _frameEvent = Kernel32.CreateAutoResetEvent();
        _wakeEvent = Kernel32.CreateAutoResetEvent();
        _frameEventRaw = _frameEvent.DangerousGetHandle();
        _wakeEventRaw = _wakeEvent.DangerousGetHandle();
        // Above the decoder: software decoding keeps every core busy, and a render thread queued behind it misses vsync.
        _thread = new Thread(static state => ((MpvRenderLoop)state!).Run())
        {
            IsBackground = true,
            Name = "Aether.Render",
            Priority = ThreadPriority.Highest,
        };
    }

    public RenderLoopState State => _state;

    /// <summary>Device generation: 1 after the first successful start, +1 per recovery.</summary>
    public int Generation => Volatile.Read(ref _generation);

    public int DeviceLossCount => Volatile.Read(ref _deviceLossCount);

    public long FramesPresented => Interlocked.Read(ref _framesPresented);

    /// <summary>Starts the render thread. Once only.</summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The render loop was already started.");
        }
        _state = RenderLoopState.Recovering;
        _thread.Start(this);
    }

    /// <summary>
    /// Posts the panel size (DIPs) and composition scale. Any thread, never blocks on the GPU; the
    /// render thread resizes between frames and also uses it for every recreated swap chain.
    /// </summary>
    public void SetTargetSize(double logicalWidth, double logicalHeight, float compositionScaleX, float compositionScaleY)
    {
        lock (_sizeLock)
        {
            _logicalWidth = logicalWidth;
            _logicalHeight = logicalHeight;
            _scaleX = compositionScaleX;
            _scaleY = compositionScaleY;
            _sizePending = true;
        }
        Wake();
    }

    /// <summary>Draws one frame even if the backend has nothing new. Any thread.</summary>
    public void RequestRedraw()
    {
        Volatile.Write(ref _redraw, 1);
        Wake();
    }

    /// <summary>
    /// Asks the thread to stop and waits up to <paramref name="timeoutMilliseconds"/>. The thread
    /// tears its device stack down itself on the way out, even if this returns false (timeout).
    /// </summary>
    public bool Stop(int timeoutMilliseconds)
    {
        _stop = true;
        Wake();
        if (Volatile.Read(ref _started) == 0 || !_thread.IsAlive)
        {
            _state = RenderLoopState.Stopped;
            return true;
        }
        return _thread.Join(timeoutMilliseconds);
    }

    /// <summary>
    /// Requests stop and returns without waiting, so it is safe on the UI thread. The render thread
    /// tears its stack down on the way out, and the events close once it has exited.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }
        _stop = true;
        Wake();
        if (Volatile.Read(ref _started) == 0)
        {
            _state = RenderLoopState.Stopped;
            VoteClose(); // no thread will ever vote
        }
        VoteClose();
    }

    private void VoteClose()
    {
        if (Interlocked.Increment(ref _closeVotes) == 2)
        {
            _frameEvent.Dispose();
            _wakeEvent.Dispose();
        }
    }

    private void Wake()
    {
        // AddRef pins the handle so Dispose on another thread can't close it mid-call.
        bool added = false;
        try
        {
            _wakeEvent.DangerousAddRef(ref added);
            Kernel32.SetEvent(_wakeEventRaw);
        }
        catch (ObjectDisposedException)
        {
            // Disposed: nothing left to wake.
        }
        finally
        {
            if (added)
            {
                _wakeEvent.DangerousRelease();
            }
        }
    }

    private void Run()
    {
        uint retryDelay = _options.RetryDelayMilliseconds;
        bool force = true;
        try
        {
            while (!_stop)
            {
                try
                {
                    if (_backend is null)
                    {
                        if (!TryCreateStack())
                        {
                            WaitForWake(retryDelay);
                            retryDelay = Math.Min(retryDelay * 2, _options.MaxRetryDelayMilliseconds);
                            continue;
                        }
                        retryDelay = _options.RetryDelayMilliseconds;
                        force = true;
                    }

                    if (!force)
                    {
                        WaitForWork();
                    }
                    if (_stop)
                    {
                        break;
                    }

                    force |= ApplyPendingSize();
                    force |= Interlocked.Exchange(ref _redraw, 0) != 0;

                    DxgiPresenter presenter = _presenter!;
                    IVideoRenderBackend backend = _backend!;
                    // Always poll: mpv requires mpv_render_context_update after every update callback.
                    bool frame = backend.PollFrame();
                    if (presenter.IsDeviceLost)
                    {
                        HandleDeviceLoss();
                        continue;
                    }
                    if (!frame && !force)
                    {
                        continue;
                    }
                    if (!presenter.WaitForNextFrame(_options.WaitTimeoutMilliseconds))
                    {
                        force = true; // swap chain busy (occluded, minimized): try again next pass
                        continue;
                    }

                    if (!backend.Render(presenter.PixelWidth, presenter.PixelHeight))
                    {
                        presenter.ReportDeviceLost(DxNative.DXGI_ERROR_DEVICE_REMOVED);
                    }
                    else if (presenter.Present(_options.VSync) != PresentResult.DeviceLost)
                    {
                        backend.ReportSwap();
                        Interlocked.Increment(ref _framesPresented);
                        force = _options.Backdrop?.NeedsFrame == true;
                    }

                    if (presenter.IsDeviceLost)
                    {
                        HandleDeviceLoss();
                    }
                }
                catch (Exception ex)
                {
                    Report(ex);
                    DestroyStack();
                    _state = RenderLoopState.Recovering;
                    WaitForWake(retryDelay);
                    retryDelay = Math.Min(retryDelay * 2, _options.MaxRetryDelayMilliseconds);
                }
            }
        }
        finally
        {
            DestroyStack();
            _state = RenderLoopState.Stopped;
            VoteClose();
        }
    }

    /// <summary>Waits for an mpv frame, a wake request, or the timeout. No allocation.</summary>
    private void WaitForWork()
    {
        nint* handles = stackalloc nint[2];
        handles[0] = _frameEventRaw;
        handles[1] = _wakeEventRaw;
        uint result = Kernel32.WaitForMultipleObjectsEx(2, handles, 0, _options.WaitTimeoutMilliseconds, 0);
        if (result == Kernel32.WAIT_FAILED)
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }

    private void WaitForWake(uint milliseconds)
    {
        nint wake = _wakeEventRaw;
        uint result = Kernel32.WaitForMultipleObjectsEx(1, &wake, 0, milliseconds, 0);
        if (result == Kernel32.WAIT_FAILED)
        {
            throw new System.ComponentModel.Win32Exception();
        }
    }

    /// <summary>Applies a posted size. True if the swap chain changed (a redraw is needed).</summary>
    private bool ApplyPendingSize()
    {
        double w, h;
        float sx, sy;
        lock (_sizeLock)
        {
            if (!_sizePending)
            {
                return false;
            }
            _sizePending = false;
            w = _logicalWidth;
            h = _logicalHeight;
            sx = _scaleX;
            sy = _scaleY;
        }

        DxgiPresenter presenter = _presenter!;
        presenter.SetTargetSize(w, h, sx, sy);
        SwapChainTargetSize size = DxgiPresenter.ComputeTargetSize(w, h, sx, sy);
        if (size.Width == presenter.PixelWidth && size.Height == presenter.PixelHeight)
        {
            return presenter.ApplyPendingResize(); // scale only: matrix transform, buffers untouched
        }

        _backend!.ReleaseBackBuffer();
        bool changed = presenter.ApplyPendingResize();
        if (!presenter.IsDeviceLost)
        {
            _backend.AcquireBackBuffer();
        }
        return changed;
    }

    private bool TryCreateStack()
    {
        D3D11DeviceManager? device = null;
        DxgiPresenter? presenter = null;
        IVideoRenderBackend? backend = null;
        try
        {
            device = _options.DeviceFactory?.Invoke(_options.Device) ?? D3D11DeviceManager.Create(_options.Device);

            double w, h;
            float sx, sy;
            lock (_sizeLock)
            {
                w = _logicalWidth;
                h = _logicalHeight;
                sx = _scaleX;
                sy = _scaleY;
                _sizePending = false;
            }
            SwapChainTargetSize size = DxgiPresenter.ComputeTargetSize(w, h, sx, sy);
            DxgiPresenterOptions template = _options.Presenter;
            presenter = new DxgiPresenter(device, new DxgiPresenterOptions
            {
                Width = size.Width,
                Height = size.Height,
                BufferCount = template.BufferCount,
                Format = template.Format,
                AlphaMode = template.AlphaMode,
                AllowTearing = template.AllowTearing,
                MaximumFrameLatency = template.MaximumFrameLatency,
                ClearStateOnResize = !_backendFactory.CachesDeviceState,
            });
            // Same pixel size, so this only queues the inverse-scale matrix: no buffer is touched.
            presenter.SetTargetSize(w, h, sx, sy);
            presenter.ApplyPendingResize();

            backend = _backendFactory.Create(device, presenter, _frameEventRaw);
        }
        catch (Exception ex)
        {
            DisposeQuietly(backend);
            DisposeQuietly(presenter);
            DisposeQuietly(device);
            _state = RenderLoopState.Recovering;
            Report(ex);
            return false;
        }

        int generation = Interlocked.Increment(ref _generation);
        _device = device;
        _presenter = presenter;
        _backend = backend;
        _state = RenderLoopState.Running;

        var handoff = new SwapChainHandoff(presenter.SwapChainPointer, generation);
        try
        {
            _host.OnSwapChainCreated(handoff);
        }
        catch (Exception ex)
        {
            // Ownership already passed; the host may have queued it. Its finalizer is the backstop.
            Report(ex);
        }
        return true;
    }

    private void HandleDeviceLoss()
    {
        int reason = _presenter?.DeviceLostReason ?? DxNative.DXGI_ERROR_DEVICE_REMOVED;
        Interlocked.Increment(ref _deviceLossCount);
        _state = RenderLoopState.Recovering;
        DestroyStack();
        try
        {
            _host.OnDeviceLost(reason, Volatile.Read(ref _generation));
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    /// <summary>Reverse creation order: backend (frees the mpv render context), presenter, device.</summary>
    private void DestroyStack()
    {
        IVideoRenderBackend? backend = _backend;
        DxgiPresenter? presenter = _presenter;
        D3D11DeviceManager? device = _device;
        _backend = null;
        _presenter = null;
        _device = null;
        DisposeQuietly(backend);
        DisposeQuietly(presenter);
        DisposeQuietly(device);
    }

    private void DisposeQuietly(IDisposable? disposable)
    {
        try
        {
            disposable?.Dispose();
        }
        catch (Exception ex)
        {
            Report(ex);
        }
    }

    private void Report(Exception ex)
    {
        try
        {
            _host.OnFault(ex);
        }
        catch
        {
            // A throwing host must not take the render thread down.
        }
    }
}
