using System;
using System.Diagnostics;
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
    private bool _videoStale = true;

    private AngleMpvRenderBackend(AngleBridge bridge, MpvRenderContext? render, BackdropSource? backdropSource, BackdropBlurPass? backdrop, VideoFrameCache? frameCache)
    {
        _bridge = bridge;
        _render = render;
        _backdropSource = backdropSource;
        _backdrop = backdrop;
        _frameCache = frameCache;
    }

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
            // Only worth its extra copy when redraws without a new video frame happen, which is what the fade causes.
            VideoFrameCache? frameCache = backdrop is not null ? VideoFrameCache.Create(bridge) : null;
            return new AngleMpvRenderBackend(bridge, render, backdropSource, backdrop, frameCache);
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
        _videoStale |= frame;
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
            RenderVideo(width, height);
            if (_backdrop is not null && _backdropSource!.TryGetFrame(Stopwatch.GetTimestamp(), out BackdropFrame frame))
            {
                _backdrop.Draw(frame, width, height);
            }
        }
        _bridge.Flush();
        return !_bridge.IsContextLost;
    }

    /// <summary>
    /// Draws the picture. A redraw without a new video frame (the card's fade) copies the cached one
    /// back instead of making mpv render it again; the cache is stale after a new frame or a resize.
    /// </summary>
    private void RenderVideo(uint width, uint height)
    {
        if (_frameCache is { } cache)
        {
            if (cache.EnsureSize(width, height, out bool reallocated))
            {
                if (_videoStale || reallocated)
                {
                    MpvException.ThrowIfError(_render!.RenderOpenGL((int)cache.Framebuffer, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
                    _videoStale = false;
                }
                cache.CopyToBackBuffer();
                return;
            }
            // The cache texture could not be built: draw straight to the back buffer from now on.
            cache.Dispose();
            _frameCache = null;
        }
        MpvException.ThrowIfError(_render!.RenderOpenGL(0, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
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
