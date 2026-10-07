using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Angle.Native;

/// <summary>
/// EGL 1.4 core exports of ANGLE's <c>libEGL.dll</c>. Extension entry points (device creation,
/// platform display) and every GL function are resolved through <c>eglGetProcAddress</c>.
/// EGLBoolean is <c>unsigned int</c>; handles are pointer-sized.
/// </summary>
internal static unsafe partial class Egl
{
    private const string Lib = NativeLibraries.LibEgl;

    static Egl() => NativeLibraries.EnsureResolver();

    public const int EGL_SUCCESS = 0x3000;
    public const int EGL_BAD_ALLOC = 0x3003;
    public const int EGL_CONTEXT_LOST = 0x300E;
    public const int EGL_NONE = 0x3038;
    public const int EGL_EXTENSIONS = 0x3055;
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_DEPTH_SIZE = 0x3025;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_OPENGL_ES2_BIT = 0x0004;
    public const int EGL_OPENGL_ES3_BIT = 0x0040;
    public const int EGL_CONTEXT_MAJOR_VERSION = 0x3098;
    public const int EGL_CONTEXT_OPENGL_RESET_NOTIFICATION_STRATEGY_EXT = 0x3138;
    public const int EGL_LOSE_CONTEXT_ON_RESET_EXT = 0x31BF;
    public const uint EGL_PLATFORM_DEVICE_EXT = 0x313F;
    public const int EGL_D3D11_DEVICE_ANGLE = 0x33A1;
    public const uint EGL_D3D_TEXTURE_ANGLE = 0x33A3;

    [LibraryImport(Lib, EntryPoint = "eglGetProcAddress")]
    public static partial void* GetProcAddress(byte* name);

    [LibraryImport(Lib, EntryPoint = "eglGetError")]
    public static partial int GetError();

    [LibraryImport(Lib, EntryPoint = "eglQueryString")]
    public static partial byte* QueryString(nint display, int name);

    [LibraryImport(Lib, EntryPoint = "eglInitialize")]
    public static partial uint Initialize(nint display, int* major, int* minor);

    [LibraryImport(Lib, EntryPoint = "eglTerminate")]
    public static partial uint Terminate(nint display);

    [LibraryImport(Lib, EntryPoint = "eglChooseConfig")]
    public static partial uint ChooseConfig(nint display, int* attribs, nint* configs, int configSize, int* numConfig);

    [LibraryImport(Lib, EntryPoint = "eglCreateContext")]
    public static partial nint CreateContext(nint display, nint config, nint shareContext, int* attribs);

    [LibraryImport(Lib, EntryPoint = "eglDestroyContext")]
    public static partial uint DestroyContext(nint display, nint context);

    [LibraryImport(Lib, EntryPoint = "eglMakeCurrent")]
    public static partial uint MakeCurrent(nint display, nint draw, nint read, nint context);

    [LibraryImport(Lib, EntryPoint = "eglCreatePbufferFromClientBuffer")]
    public static partial nint CreatePbufferFromClientBuffer(nint display, uint bufferType, nint buffer, nint config, int* attribs);

    [LibraryImport(Lib, EntryPoint = "eglDestroySurface")]
    public static partial uint DestroySurface(nint display, nint surface);

    [LibraryImport(Lib, EntryPoint = "eglReleaseThread")]
    public static partial uint ReleaseThread();

    /// <summary>True if the space-separated extension string <paramref name="extensions"/> contains <paramref name="name"/>.</summary>
    public static bool HasExtension(byte* extensions, ReadOnlySpan<byte> name)
    {
        if (extensions == null)
        {
            return false;
        }
        var all = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(extensions);
        while (!all.IsEmpty)
        {
            int space = all.IndexOf((byte)' ');
            ReadOnlySpan<byte> token = space < 0 ? all : all[..space];
            if (token.SequenceEqual(name))
            {
                return true;
            }
            all = space < 0 ? default : all[(space + 1)..];
        }
        return false;
    }
}
