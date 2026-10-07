using System;
using Aether.Core.Interop.DirectX;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// Draws frames into a <see cref="DxgiPresenter"/>'s back buffer for <see cref="MpvRenderLoop"/>.
/// One instance per device generation; every member runs on the render thread.
/// </summary>
public interface IVideoRenderBackend : IDisposable
{
    /// <summary>Called on every loop wake. True if a new frame is ready (e.g. <c>mpv_render_context_update</c> set FRAME).</summary>
    bool PollFrame();

    /// <summary>Drop every reference to the back buffer; <c>ResizeBuffers</c> follows.</summary>
    void ReleaseBackBuffer();

    /// <summary>Re-wrap the (resized) back buffer.</summary>
    void AcquireBackBuffer();

    /// <summary>
    /// Draws into the back buffer and submits the work to the immediate context, ready for Present.
    /// Returns false if the GPU context was lost. Must not allocate.
    /// </summary>
    bool Render(uint width, uint height);

    /// <summary>Called right after a successful Present.</summary>
    void ReportSwap();
}

/// <summary>Creates a backend for each device generation (initial start and every device-loss recovery).</summary>
public interface IVideoRenderBackendFactory
{
    /// <summary>
    /// True if the backend caches D3D11 pipeline state on the shared immediate context (ANGLE). The
    /// loop then creates presenters with <see cref="DxgiPresenterOptions.ClearStateOnResize"/> off.
    /// </summary>
    bool CachesDeviceState { get; }

    /// <summary>
    /// Render thread. <paramref name="frameReadyEvent"/> is a kernel auto-reset event that wakes the
    /// loop; it stays valid until the backend is disposed.
    /// </summary>
    IVideoRenderBackend Create(D3D11DeviceManager device, DxgiPresenter presenter, nint frameReadyEvent);
}
