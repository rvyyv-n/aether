using System;
using Aether.Core.Interop.Angle;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// The video path (ADR-0007): ANGLE on our device wraps the back buffer, and an mpv <c>opengl</c>
/// render context draws into it. With <paramref name="mpv"/> null it clears through ANGLE instead
/// (same device sharing and resize sequence, no libmpv needed).
/// </summary>
public sealed class AngleMpvRenderBackendFactory(MpvContext? mpv) : IVideoRenderBackendFactory
{
    public bool CachesDeviceState => true;

    public IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent) =>
        AngleMpvRenderBackend.Create(device, presenter, mpv, frameReadyEvent);
}

internal sealed unsafe class AngleMpvRenderBackend : IVideoRenderBackend
{
    private readonly AngleBridge _bridge;
    private readonly MpvRenderContext? _render;

    private AngleMpvRenderBackend(AngleBridge bridge, MpvRenderContext? render)
    {
        _bridge = bridge;
        _render = render;
    }

    public static AngleMpvRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, MpvContext? mpv, nint frameReadyEvent)
    {
        // Loads libEGL.dll first, so mpv's own LoadLibrary("libEGL.dll") binds to this module.
        AngleBridge bridge = AngleBridge.Create(device, presenter);
        try
        {
            // The context is current; mpv's d3d11va interop finds our device through the EGL display.
            MpvRenderContext? render = mpv is null
                ? null
                : MpvRenderContext.CreateOpenGL(mpv, AngleBridge.GetProcAddressCallback, bridge.GetProcAddressContext, frameReadyEvent);
            return new AngleMpvRenderBackend(bridge, render);
        }
        catch
        {
            bridge.Dispose();
            throw;
        }
    }

    public bool PollFrame() => _render is not null && (_render.Update() & MpvRenderUpdateFlags.Frame) != 0;

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
            MpvException.ThrowIfError(_render.RenderOpenGL(0, (int)width, (int)height, AngleBridge.RenderFlipY), "mpv_render_context_render");
        }
        _bridge.Flush();
        return !_bridge.IsContextLost;
    }

    public void ReportSwap() => _render?.ReportSwap();

    public void Dispose()
    {
        try
        {
            // mpv frees its GL objects through the context, so it must be current (surfaceless is fine).
            _bridge.MakeContextCurrent();
            _render?.Dispose();
        }
        finally
        {
            _bridge.Dispose();
        }
    }
}
