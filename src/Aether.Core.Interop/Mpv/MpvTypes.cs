using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Mpv;

[StructLayout(LayoutKind.Sequential)]
public struct MpvRenderParam
{
    public MpvRenderParamType Type;
    public IntPtr Data;
}

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
