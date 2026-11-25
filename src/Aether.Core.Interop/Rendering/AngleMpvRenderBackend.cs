using System;
using System.Diagnostics;
using System.Threading;
using Aether.Core.Interop.Angle;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// The video path (ADR-0007): ANGLE on our device wraps the back buffer, and an mpv <c>opengl</c>
/// render context draws into it. With <paramref name="mpv"/> null it clears through ANGLE instead
/// (same device sharing and resize sequence, no libmpv needed).
/// </summary>
public sealed class AngleMpvRenderBackendFactory(MpvContext? mpv, BackdropSource? backdrop = null) : IVideoRenderBackendFactory
{
    public bool CachesDeviceState => true;

    public IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent) =>
        AngleMpvRenderBackend.Create(device, presenter, mpv, backdrop, frameReadyEvent);
}

internal sealed unsafe class AngleMpvRenderBackend : IVideoRenderBackend
{
    private readonly AngleBridge _bridge;
    private readonly MpvRenderContext? _render;
    private readonly BackdropSource? _backdropSource;
    private readonly BackdropBlurPass? _backdrop;

    private VideoFrameCache? _frameCache;
    private bool _frameCacheFailed;
    private bool _videoStale = true;
    private bool _cacheValid;
    private long _blurTicksTotal;
    private long _blurFrames;

    private AngleMpvRenderBackend(AngleBridge bridge, MpvRenderContext? render, BackdropSource? backdropSource, BackdropBlurPass? backdrop)
    {
        _bridge = bridge;
        _render = render;
        _backdropSource = backdropSource;
        _backdrop = backdrop;
    }

    public double AverageBlurPassMilliseconds
    {
        get
        {
            long frames = Volatile.Read(ref _blurFrames);
            return frames > 0 ? Volatile.Read(ref _blurTicksTotal) * 1000.0 / Stopwatch.Frequency / frames : -1;
        }
    }

    public long BlurPassFrames => Volatile.Read(ref _blurFrames);

    public static AngleMpvRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, MpvContext? mpv, BackdropSource? backdropSource, nint frameReadyEvent)
    {
        // Loads libEGL.dll first, so mpv's own LoadLibrary("libEGL.dll") binds to this module.
        AngleBridge bridge = AngleBridge.Create(device, presenter);
        try
        {
            // The context is current; mpv's d3d11va interop finds our device through the EGL display.
            MpvRenderContext? render = mpv is null
                ? null
                : MpvRenderContext.CreateOpenGL(mpv, AngleBridge.GetProcAddressCallback, bridge.GetProcAddressContext, frameReadyEvent);
            // Only a video frame has a card to blur behind; a null pass leaves the plain tint.
            BackdropBlurPass? backdrop = render is not null && backdropSource is not null ? BackdropBlurPass.Create(bridge) : null;
            return new AngleMpvRenderBackend(bridge, render, backdropSource, backdrop);
        }
        catch
        {
            bridge.Dispose();
            throw;
        }
    }

    public bool PollFrame()
    {
        bool frame = _render is not null && (_render.Update() & MpvRenderUpdateFlags.Frame) != 0;
        if (frame)
        {
            _videoStale = true;
            _cacheValid = false;
        }
        return frame;
    }

    public void ReleaseBackBuffer() => _bridge.ReleaseBackBuffer();

    public void AcquireBackBuffer() => _bridge.AcquireBackBuffer();

    public bool Render(uint width, uint height)
    {
        if (_render is null)
        {
            _bridge.Clear(0f, 0f, 0f, 1f);
        }
        else
        {
            Span<BackdropFrame> frames = stackalloc BackdropFrame[BackdropSource.SlotCount];
            int count = _backdrop is not null ? _backdropSource!.TryGetFrames(Stopwatch.GetTimestamp(), frames) : 0;
            RenderVideo(width, height, needCache: count > 0);
            if (count > 0)
            {
                long start = Stopwatch.GetTimestamp();
                _backdrop!.Draw(frames[..count], width, height);
                long elapsed = Stopwatch.GetTimestamp() - start;
                Interlocked.Add(ref _blurTicksTotal, elapsed);
                Interlocked.Increment(ref _blurFrames);
            }
        }
        _bridge.Flush();
        return !_bridge.IsContextLost;
    }

    /// <summary>
    /// Draws the picture. When no blurred card is visible or fading, renders directly into the swap
    /// chain back buffer (zero extra copy). When blur overlays are active, caches the rendered frame
    /// so UI fades while paused reuse it instead of making mpv re-render every vsync.
    /// </summary>
    private void RenderVideo(uint width, uint height, bool needCache)
    {
        if (!needCache)
        {
            if (!_videoStale && _cacheValid && _frameCache is { } cached
                && cached.Width == (int)width && cached.Height == (int)height)
            {
                cached.CopyToBackBuffer();
                return;
            }
            MpvException.ThrowIfError(_render!.RenderOpenGL(0, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
            _videoStale = false;
            _cacheValid = false;
            return;
        }

        if (_frameCache is null && !_frameCacheFailed)
        {
            _frameCache = VideoFrameCache.Create(_bridge);
            if (_frameCache is null)
            {
                _frameCacheFailed = true;
            }
        }

        if (_frameCache is { } cache)
        {
            if (cache.EnsureSize(width, height, out bool reallocated))
            {
                if (_videoStale || reallocated || !_cacheValid)
                {
                    MpvException.ThrowIfError(_render!.RenderOpenGL((int)cache.Framebuffer, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
                    _videoStale = false;
                    _cacheValid = true;
                }
                cache.CopyToBackBuffer();
                return;
            }
            // The cache texture could not be built: draw straight to the back buffer from now on.
            cache.Dispose();
            _frameCache = null;
            _frameCacheFailed = true;
        }
        MpvException.ThrowIfError(_render!.RenderOpenGL(0, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
        _videoStale = false;
        _cacheValid = false;
    }

    public void ReportSwap() => _render?.ReportSwap();

    public void Dispose()
    {
        try
        {
            // mpv frees its GL objects through the context, so it must be current (surfaceless is fine).
            _bridge.MakeContextCurrent();
            _backdrop?.Dispose();
            _frameCache?.Dispose();
            _render?.Dispose();
        }
        finally
        {
            _bridge.Dispose();
        }
    }
}
