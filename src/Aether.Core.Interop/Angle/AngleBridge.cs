using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.Angle.Native;
using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.DirectX.Native;

namespace Aether.Core.Interop.Angle;

/// <summary>
/// ANGLE (OpenGL ES on Direct3D 11) bound to our own device, rendering straight into the swap
/// chain. The EGL display is created on <see cref="D3D11DeviceManager.DevicePointer"/>
/// (<c>EGL_ANGLE_device_creation</c>), and swap chain buffer 0 is wrapped as a pbuffer
/// (<c>EGL_ANGLE_d3d_texture_client_buffer</c>). With that pbuffer current, GL framebuffer 0 is
/// the back buffer: whatever mpv draws there is what <see cref="DxgiPresenter.Present"/> shows.
/// <para>
/// Threading: everything here belongs to the render thread, which keeps the context current from
/// <see cref="Create"/> to <see cref="Dispose"/>. EGL contexts are bound per thread.
/// </para>
/// <para>
/// ANGLE caches D3D11 pipeline state on the shared immediate context, so the presenter must be
/// created with <see cref="DxgiPresenterOptions.ClearStateOnResize"/> off and its <c>Clear</c> left
/// unused. Resizing is <see cref="ReleaseBackBuffer"/> → <see cref="DxgiPresenter.ApplyPendingResize"/>
/// → <see cref="AcquireBackBuffer"/>.
/// </para>
/// </summary>
public sealed unsafe class AngleBridge : IDisposable
{
    private const uint GL_COLOR_BUFFER_BIT = 0x4000;

    /// <summary>
    /// Value for <c>MPV_RENDER_PARAM_FLIP_Y</c>. ANGLE stores a client-buffer pbuffer without the
    /// window-surface flip: GL row 0 lands in the texture's top row (measured by readback on hardware
    /// and WARP), which is what mpv's unflipped FBO output expects.
    /// </summary>
    public const bool RenderFlipY = false;

    private readonly DxgiPresenter _presenter;
    private nint _eglDevice;
    private nint _display;
    private nint _config;
    private nint _context;
    private nint _surface;
    private readonly void* _eglGetProcAddress;
    private readonly delegate* unmanaged[Stdcall]<nint, uint> _releaseDevice;
    private readonly delegate* unmanaged[Stdcall]<void> _glFlush;
    private readonly delegate* unmanaged[Stdcall]<float, float, float, float, void> _glClearColor;
    private readonly delegate* unmanaged[Stdcall]<uint, void> _glClear;
    private readonly delegate* unmanaged[Stdcall]<int, int, int, int, void> _glViewport;
    private readonly delegate* unmanaged[Stdcall]<uint> _glGetGraphicsResetStatus; // null without robustness

    private AngleBridge(DxgiPresenter presenter, void* eglGetProcAddress, delegate* unmanaged[Stdcall]<nint, uint> releaseDevice,
        nint eglDevice, nint display, nint config, nint context, bool robust)
    {
        _presenter = presenter;
        _eglGetProcAddress = eglGetProcAddress;
        _releaseDevice = releaseDevice;
        _eglDevice = eglDevice;
        _display = display;
        _config = config;
        _context = context;
        _glFlush = (delegate* unmanaged[Stdcall]<void>)RequireProc("glFlush"u8);
        _glClearColor = (delegate* unmanaged[Stdcall]<float, float, float, float, void>)RequireProc("glClearColor"u8);
        _glClear = (delegate* unmanaged[Stdcall]<uint, void>)RequireProc("glClear"u8);
        _glViewport = (delegate* unmanaged[Stdcall]<int, int, int, int, void>)RequireProc("glViewport"u8);
        if (robust)
        {
            void* reset = Proc("glGetGraphicsResetStatusEXT"u8);
            _glGetGraphicsResetStatus = (delegate* unmanaged[Stdcall]<uint>)(reset != null ? reset : Proc("glGetGraphicsResetStatus"u8));
        }
    }

    ~AngleBridge() => ReleaseAll();

    /// <summary>
    /// Thunk for <c>mpv_opengl_init_params.get_proc_address</c>; pass <see cref="GetProcAddressContext"/> as its context.
    /// </summary>
    public static delegate* unmanaged[Cdecl]<void*, byte*, void*> GetProcAddressCallback => &GetProcAddressThunk;

    /// <summary>Address of ANGLE's <c>eglGetProcAddress</c>, forwarded to by <see cref="GetProcAddressCallback"/>.</summary>
    public void* GetProcAddressContext => _eglGetProcAddress;

    public nint Display => _display;

    public nint Context => _context;

    /// <summary>The pbuffer wrapping the back buffer, or 0 between <see cref="ReleaseBackBuffer"/> and <see cref="AcquireBackBuffer"/>.</summary>
    public nint Surface => _surface;

    /// <summary>True if the context reports GPU resets (<c>EGL_EXT_create_context_robustness</c>).</summary>
    public bool IsRobust => _glGetGraphicsResetStatus != null;

    public bool IsDisposed => _display == 0;

    /// <summary>
    /// Creates the display on the presenter's device, an ES 3 (else ES 2) context and the back-buffer
    /// pbuffer, and makes them current on the calling thread.
    /// </summary>
    /// <exception cref="NotSupportedException">The loaded ANGLE lacks a required extension.</exception>
    /// <exception cref="InvalidOperationException">An EGL call failed (message carries the EGL error).</exception>
    public static AngleBridge Create(D3D11DeviceManager device, DxgiPresenter presenter)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(presenter);

        byte* clientExtensions = Egl.QueryString(0, Egl.EGL_EXTENSIONS);
        RequireExtension(clientExtensions, "EGL_ANGLE_device_creation"u8);
        RequireExtension(clientExtensions, "EGL_ANGLE_device_creation_d3d11"u8);
        RequireExtension(clientExtensions, "EGL_EXT_platform_device"u8);

        void* getProc = (void*)NativeLibraries.GetExport(NativeLibraries.LibEgl, "eglGetProcAddress");
        var createDevice = (delegate* unmanaged[Stdcall]<int, nint, nint*, nint>)RequireEglProc("eglCreateDeviceANGLE"u8);
        var releaseDevice = (delegate* unmanaged[Stdcall]<nint, uint>)RequireEglProc("eglReleaseDeviceANGLE"u8);
        var getPlatformDisplay = (delegate* unmanaged[Stdcall]<uint, nint, int*, nint>)RequireEglProc("eglGetPlatformDisplayEXT"u8);

        nint eglDevice = 0, display = 0, context = 0;
        bool initialized = false;
        try
        {
            // ANGLE AddRefs the ID3D11Device for the EGLDevice's lifetime.
            eglDevice = createDevice(Egl.EGL_D3D11_DEVICE_ANGLE, device.DevicePointer, null);
            ThrowIfNull(eglDevice, "eglCreateDeviceANGLE");

            int* noAttribs = stackalloc int[] { Egl.EGL_NONE };
            display = getPlatformDisplay(Egl.EGL_PLATFORM_DEVICE_EXT, eglDevice, noAttribs);
            ThrowIfNull(display, "eglGetPlatformDisplayEXT");

            int major, minor;
            ThrowIfFalse(Egl.Initialize(display, &major, &minor), "eglInitialize");
            initialized = true;

            byte* displayExtensions = Egl.QueryString(display, Egl.EGL_EXTENSIONS);
            RequireExtension(displayExtensions, "EGL_ANGLE_d3d_texture_client_buffer"u8);
            bool robust = Egl.HasExtension(displayExtensions, "EGL_EXT_create_context_robustness"u8);

            nint config = ChooseConfig(display);
            context = CreateContext(display, config, robust);

            var bridge = new AngleBridge(presenter, getProc, releaseDevice, eglDevice, display, config, context, robust);
            try
            {
                bridge.AcquireBackBuffer();
            }
            catch
            {
                bridge.Dispose();
                throw;
            }
            return bridge;
        }
        catch
        {
            if (context != 0)
            {
                Egl.MakeCurrent(display, 0, 0, 0);
                Egl.DestroyContext(display, context);
            }
            if (initialized)
            {
                Egl.Terminate(display);
            }
            if (eglDevice != 0)
            {
                releaseDevice(eglDevice);
            }
            throw;
        }
    }

    /// <summary>
    /// Wraps the presenter's current buffer 0 in a pbuffer and makes it current. Call after
    /// creation and after every <see cref="DxgiPresenter.ApplyPendingResize"/> that followed
    /// <see cref="ReleaseBackBuffer"/>.
    /// </summary>
    public void AcquireBackBuffer()
    {
        nint display = Live();
        if (_surface != 0)
        {
            return;
        }

        nint texture = _presenter.GetBackBufferTexture();
        nint surface;
        try
        {
            // Size and format come from the texture; ANGLE takes its own reference to it.
            int* attribs = stackalloc int[] { Egl.EGL_NONE };
            surface = Egl.CreatePbufferFromClientBuffer(display, Egl.EGL_D3D_TEXTURE_ANGLE, texture, _config, attribs);
        }
        finally
        {
            Com.Release(texture);
        }
        ThrowIfNull(surface, "eglCreatePbufferFromClientBuffer");

        if (Egl.MakeCurrent(display, surface, surface, _context) == 0)
        {
            int error = Egl.GetError();
            Egl.DestroySurface(display, surface);
            throw new InvalidOperationException($"eglMakeCurrent failed: EGL error 0x{error:X4}.");
        }
        _surface = surface;
        _glViewport(0, 0, (int)_presenter.PixelWidth, (int)_presenter.PixelHeight);
    }

    /// <summary>
    /// Unbinds the context and destroys the pbuffer, dropping ANGLE's references to the back buffer
    /// so <c>ResizeBuffers</c> can run. The context stays alive (mpv's GL objects survive).
    /// </summary>
    public void ReleaseBackBuffer()
    {
        nint display = Live();
        nint surface = _surface;
        if (surface == 0)
        {
            return;
        }
        _surface = 0;
        // Without a current context ANGLE flushes and unbinds the surface's render target.
        Egl.MakeCurrent(display, 0, 0, 0);
        Egl.DestroySurface(display, surface);
    }

    /// <summary>
    /// Makes the context current with no surface (between frames or for teardown). Needed before
    /// freeing an mpv render context while the back buffer is released.
    /// </summary>
    public bool MakeContextCurrent() =>
        _surface != 0
            ? Egl.MakeCurrent(Live(), _surface, _surface, _context) != 0
            : Egl.MakeCurrent(Live(), 0, 0, _context) != 0;

    /// <summary><c>glFlush</c>: submits ANGLE's batched work to the D3D11 immediate context. Call before Present.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush() => _glFlush();

    /// <summary>Clears framebuffer 0 (the back buffer). For frames drawn without mpv.</summary>
    public void Clear(float r, float g, float b, float a)
    {
        _glClearColor(r, g, b, a);
        _glClear(GL_COLOR_BUFFER_BIT);
    }

    /// <summary>
    /// True once the GPU reset the context (device removed/reset). Only robust contexts report it;
    /// otherwise device loss surfaces through <see cref="DxgiPresenter.Present"/>.
    /// </summary>
    public bool IsContextLost => _glGetGraphicsResetStatus != null && _glGetGraphicsResetStatus() != 0;

    private nint Live() => _display != 0 ? _display : throw new ObjectDisposedException(nameof(AngleBridge));

    /// <summary>A GL entry point of the current context by name, or null.</summary>
    internal void* GlProc(ReadOnlySpan<byte> name) => Proc(name);

    private void* Proc(ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
        {
            return ((delegate* unmanaged[Stdcall]<byte*, void*>)_eglGetProcAddress)(p);
        }
    }

    private void* RequireProc(ReadOnlySpan<byte> name)
    {
        void* proc = Proc(name);
        return proc != null ? proc : throw new NotSupportedException($"ANGLE does not export {System.Text.Encoding.ASCII.GetString(name)}.");
    }

    private static nint ChooseConfig(nint display)
    {
        int* attribs = stackalloc int[]
        {
            Egl.EGL_RED_SIZE, 8, Egl.EGL_GREEN_SIZE, 8, Egl.EGL_BLUE_SIZE, 8, Egl.EGL_ALPHA_SIZE, 8,
            Egl.EGL_DEPTH_SIZE, 0, Egl.EGL_STENCIL_SIZE, 0,
            Egl.EGL_SURFACE_TYPE, Egl.EGL_PBUFFER_BIT,
            Egl.EGL_RENDERABLE_TYPE, Egl.EGL_OPENGL_ES2_BIT,
            Egl.EGL_NONE,
        };
        nint config = 0;
        int count = 0;
        ThrowIfFalse(Egl.ChooseConfig(display, attribs, &config, 1, &count), "eglChooseConfig");
        if (count < 1 || config == 0)
        {
            throw new NotSupportedException("ANGLE offers no RGBA8 pbuffer config.");
        }
        return config;
    }

    private static nint CreateContext(nint display, nint config, bool robust)
    {
        int* attribs = stackalloc int[5];
        foreach (int version in (ReadOnlySpan<int>)[3, 2])
        {
            int n = 0;
            attribs[n++] = Egl.EGL_CONTEXT_MAJOR_VERSION;
            attribs[n++] = version;
            if (robust)
            {
                attribs[n++] = Egl.EGL_CONTEXT_OPENGL_RESET_NOTIFICATION_STRATEGY_EXT;
                attribs[n++] = Egl.EGL_LOSE_CONTEXT_ON_RESET_EXT;
            }
            attribs[n] = Egl.EGL_NONE;
            nint context = Egl.CreateContext(display, config, 0, attribs);
            if (context != 0)
            {
                return context;
            }
        }
        throw new InvalidOperationException($"eglCreateContext failed: EGL error 0x{Egl.GetError():X4}.");
    }

    private static void* RequireEglProc(ReadOnlySpan<byte> name)
    {
        fixed (byte* p = name)
        {
            void* proc = Egl.GetProcAddress(p);
            return proc != null ? proc : throw new NotSupportedException($"ANGLE lacks {System.Text.Encoding.ASCII.GetString(name)}.");
        }
    }

    private static void RequireExtension(byte* extensions, ReadOnlySpan<byte> name)
    {
        if (!Egl.HasExtension(extensions, name))
        {
            throw new NotSupportedException($"ANGLE lacks {System.Text.Encoding.ASCII.GetString(name)}.");
        }
    }

    private static void ThrowIfNull(nint handle, string call)
    {
        if (handle == 0)
        {
            throw new InvalidOperationException($"{call} failed: EGL error 0x{Egl.GetError():X4}.");
        }
    }

    private static void ThrowIfFalse(uint result, string call)
    {
        if (result == 0)
        {
            throw new InvalidOperationException($"{call} failed: EGL error 0x{Egl.GetError():X4}.");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void* GetProcAddressThunk(void* ctx, byte* name) =>
        ctx == null || name == null ? null : ((delegate* unmanaged[Stdcall]<byte*, void*>)ctx)(name);

    /// <summary>Render thread. Unbinds and destroys surface, context, display and the EGL device.</summary>
    public void Dispose()
    {
        ReleaseAll();
        GC.SuppressFinalize(this);
    }

    private void ReleaseAll()
    {
        nint display = _display;
        if (display == 0)
        {
            return;
        }
        _display = 0;

        // Finalizer path: the context may be current on another (dead) thread; unbinding here is
        // harmless, and eglTerminate defers destruction of anything still current elsewhere.
        Egl.MakeCurrent(display, 0, 0, 0);
        if (_surface != 0)
        {
            Egl.DestroySurface(display, _surface);
            _surface = 0;
        }
        if (_context != 0)
        {
            Egl.DestroyContext(display, _context);
            _context = 0;
        }
        Egl.Terminate(display);
        Egl.ReleaseThread();
        nint eglDevice = _eglDevice;
        _eglDevice = 0;
        if (eglDevice != 0)
        {
            _releaseDevice(eglDevice);
        }
    }
}
