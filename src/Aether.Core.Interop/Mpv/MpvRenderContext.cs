using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Mpv;

/// <summary>
/// Owns an <c>mpv_render_context*</c> and the core reference that keeps its <c>mpv_handle</c> alive.
/// Release frees the render context first, then drops the core reference, which is the order
/// libmpv requires.
/// </summary>
public sealed class MpvRenderContextHandle : SafeHandle
{
    private readonly MpvHandle _core;

    internal MpvRenderContextHandle(nint renderContext, MpvHandle core) : base(IntPtr.Zero, ownsHandle: true)
    {
        _core = core;
        SetHandle(renderContext);
    }

    public override bool IsInvalid => handle == IntPtr.Zero;

    protected override bool ReleaseHandle()
    {
        // Normally reached from MpvRenderContext.Dispose on the render thread with the GL context
        // current. From the finalizer no context is current; ANGLE turns the GL calls into no-ops,
        // which leaks GPU objects but still unblocks mpv_terminate_destroy.
        MpvApi.RenderContextFree(handle);
        _core.DangerousRelease();
        return true;
    }
}

/// <summary>
/// libmpv render context (render.h). Creation is API-agnostic (<see cref="Create"/> takes any
/// parameter block); <see cref="CreateOpenGL"/> is the path in use. Mainline libmpv exposes only
/// the <c>opengl</c> and <c>sw</c> render APIs, so Direct3D 11 output goes through ANGLE (ADR-0007).
/// <para>
/// Threading: create, <see cref="Update"/>, <see cref="RenderOpenGL"/>, <see cref="ReportSwap"/> and
/// <see cref="Dispose"/> belong to the render thread, with the GL context current. The update
/// callback runs on arbitrary mpv threads; it only signals a kernel event (<c>SetEvent</c>), so no
/// managed state or delegate is touched there.
/// </para>
/// </summary>
public sealed unsafe class MpvRenderContext : IDisposable
{
    private readonly MpvRenderContextHandle _handle;
    private readonly MpvRenderParamBlock _renderParams;
    private readonly MpvOpenGLFbo* _fbo;
    private readonly int* _flipY;
    private nint _ctx;

    private MpvRenderContext(MpvRenderContextHandle handle, string apiType)
    {
        _handle = handle;
        _ctx = handle.DangerousGetHandle();
        ApiType = apiType;

        // Preallocated once: each frame only rewrites the FBO size and flip flag in place.
        _renderParams = new MpvRenderParamBlock(paramCapacity: 2, payloadCapacity: 64);
        _fbo = _renderParams.Add(MpvRenderParamType.OpenGLFbo, default(MpvOpenGLFbo));
        _flipY = _renderParams.Add(MpvRenderParamType.FlipY, 0);
    }

    /// <summary><c>MPV_RENDER_API_TYPE_*</c> this context was created with.</summary>
    public string ApiType { get; }

    public bool IsDisposed => _ctx == 0;

    public MpvRenderContextHandle Handle => _handle;

    private nint Ctx => _ctx != 0 ? _ctx : throw new ObjectDisposedException(nameof(MpvRenderContext));

    /// <summary>
    /// <c>mpv_render_context_create</c> with a caller-built parameter block (which must include
    /// <c>MPV_RENDER_PARAM_API_TYPE</c>). The block is only read during the call.
    /// </summary>
    /// <param name="updateEvent">
    /// Kernel event handle signalled by mpv's update callback, or 0 for none. Must outlive the
    /// returned context (libmpv stops calling back once <c>mpv_render_context_free</c> returns).
    /// </param>
    public static MpvRenderContext Create(MpvContext mpv, MpvRenderParamBlock createParams, string apiType, nint updateEvent)
    {
        ArgumentNullException.ThrowIfNull(mpv);
        ArgumentNullException.ThrowIfNull(createParams);

        MpvHandle core = mpv.Handle;
        bool added = false;
        core.DangerousAddRef(ref added);
        MpvRenderContextHandle? handle = null;
        try
        {
            int err = MpvApi.RenderContextCreate(out nint ctx, core.DangerousGetHandle(), createParams.Parameters);
            MpvException.ThrowIfError(err, "mpv_render_context_create");
            if (ctx == 0)
            {
                throw new MpvException((int)MpvError.Generic, "mpv_render_context_create");
            }
            // From here the handle owns both the context and the core reference.
            handle = new MpvRenderContextHandle(ctx, core);
            added = false;

            if (updateEvent != 0)
            {
                MpvApi.RenderContextSetUpdateCallback(ctx, &OnUpdate, updateEvent);
            }
            return new MpvRenderContext(handle, apiType);
        }
        catch
        {
            handle?.Dispose();
            if (added)
            {
                core.DangerousRelease();
            }
            throw;
        }
    }

    /// <summary>
    /// Creates an <c>opengl</c> render context. The GL context (ANGLE) must be current on the
    /// calling thread; mpv resolves GL entry points through <paramref name="getProcAddress"/>.
    /// </summary>
    public static MpvRenderContext CreateOpenGL(MpvContext mpv,
        delegate* unmanaged[Cdecl]<void*, byte*, void*> getProcAddress, void* getProcAddressContext, nint updateEvent)
    {
        if (getProcAddress == null)
        {
            throw new ArgumentNullException(nameof(getProcAddress));
        }

        using var create = new MpvRenderParamBlock(paramCapacity: 2, payloadCapacity: 64);
        create.AddString(MpvRenderParamType.ApiType, "opengl"u8);
        create.Add(MpvRenderParamType.OpenGLInitParams, new MpvOpenGLInitParams
        {
            GetProcAddress = getProcAddress,
            GetProcAddressContext = getProcAddressContext,
        });
        return Create(mpv, create, "opengl", updateEvent);
    }

    /// <summary><c>mpv_render_context_update</c>: call after the update event fires; render when <see cref="MpvRenderUpdateFlags.Frame"/> is set.</summary>
    public MpvRenderUpdateFlags Update() => (MpvRenderUpdateFlags)MpvApi.RenderContextUpdate(Ctx);

    /// <summary>
    /// Renders the current video frame into GL framebuffer <paramref name="fbo"/> (0 = the current
    /// draw surface). No allocation; returns the mpv error code (negative on failure).
    /// </summary>
    public int RenderOpenGL(int fbo, int width, int height, bool flipY)
    {
        nint ctx = Ctx;
        _fbo->Fbo = fbo;
        _fbo->Width = width;
        _fbo->Height = height;
        _fbo->InternalFormat = 0;
        *_flipY = flipY ? 1 : 0;
        return MpvApi.RenderContextRender(ctx, _renderParams.Parameters);
    }

    /// <summary><c>mpv_render_context_report_swap</c>: call right after the frame was presented.</summary>
    public void ReportSwap() => MpvApi.RenderContextReportSwap(Ctx);

    /// <summary>Render thread, GL context current. Frees the render context, then the parameter block.</summary>
    public void Dispose()
    {
        if (_ctx == 0)
        {
            return;
        }
        _ctx = 0;
        _handle.Dispose();
        _renderParams.Dispose();
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnUpdate(nint eventHandle) => Kernel32.SetEvent(eventHandle);
}
