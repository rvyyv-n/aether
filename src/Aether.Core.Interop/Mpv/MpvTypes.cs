using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

/// <summary><c>mpv_render_param</c>: a type tag and a pointer to its payload.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MpvRenderParam
{
    public MpvRenderParamType Type;
    public IntPtr Data;
}

/// <summary><c>mpv_render_param_type</c> (render.h).</summary>
public enum MpvRenderParamType
{
    Invalid = 0,
    ApiType = 1,
    OpenGLInitParams = 2,
    OpenGLFbo = 3,
    FlipY = 4,
    Depth = 5,
    IccProfile = 6,
    AmbientLight = 7,
    X11Display = 8,
    WlDisplay = 9,
    AdvancedControl = 10,
    NextFrameInfo = 11,
    BlockForTargetTime = 12,
    SkipRendering = 13,
    DrmDisplay = 14,
    DrmDrawSurfaceSize = 15,
    DrmDisplayV2 = 16,
    SwSize = 17,
    SwFormat = 18,
    SwStride = 19,
    SwPointer = 20
}

/// <summary><c>mpv_render_update_flag</c>, returned by <c>mpv_render_context_update</c>.</summary>
[Flags]
public enum MpvRenderUpdateFlags : ulong
{
    None = 0,
    Frame = 1,
}

/// <summary><c>mpv_opengl_init_params</c> (render_gl.h, API 2.x).</summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MpvOpenGLInitParams
{
    public delegate* unmanaged[Cdecl]<void*, byte*, void*> GetProcAddress;
    public void* GetProcAddressContext;
}

/// <summary><c>mpv_opengl_fbo</c>: target framebuffer for <c>mpv_render_context_render</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct MpvOpenGLFbo
{
    public int Fbo;
    public int Width;
    public int Height;
    /// <summary>GL internal format of the target, or 0 for unknown.</summary>
    public int InternalFormat;
}

/// <summary><c>mpv_error</c> (client.h).</summary>
public enum MpvError
{
    Success = 0,
    EventQueueFull = -1,
    NoMemory = -2,
    Uninitialized = -3,
    InvalidParameter = -4,
    OptionNotFound = -5,
    OptionFormat = -6,
    OptionError = -7,
    PropertyNotFound = -8,
    PropertyFormat = -9,
    PropertyUnavailable = -10,
    PropertyError = -11,
    Command = -12,
    LoadingFailed = -13,
    AoInitFailed = -14,
    VoInitFailed = -15,
    NothingToPlay = -16,
    UnknownFormat = -17,
    Unsupported = -18,
    NotImplemented = -19,
    Generic = -20,
}

public enum MpvFormat
{
    None = 0,
    String = 1,
    OsdString = 2,
    Flag = 3,
    Int64 = 4,
    Double = 5,
    Node = 6
}

[StructLayout(LayoutKind.Sequential)]
public struct MpvEvent
{
    public int EventId;
    public int Error;
    public ulong ReplyUserdata;
    public IntPtr Data;
}
