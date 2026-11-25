using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.DirectX;

public enum D3DDriverType
{
    Hardware = 1,
    Reference = 2,
    Null = 3,
    Software = 4,
    Warp = 5,
}

public enum D3DFeatureLevel
{
    Level10_0 = 0xA000,
    Level10_1 = 0xA100,
    Level11_0 = 0xB000,
    Level11_1 = 0xB100,
}

[Flags]
public enum D3D11CreateDeviceFlags : uint
{
    None = 0,
    SingleThreaded = 0x1,
    Debug = 0x2,
    BgraSupport = 0x20,
    VideoSupport = 0x800,
}

public enum DxgiFormat
{
    Unknown = 0,
    R8G8B8A8Unorm = 28,
    B8G8R8A8Unorm = 87,
}

public enum DxgiScaling
{
    Stretch = 0,
    None = 1,
    AspectRatioStretch = 2,
}

public enum DxgiSwapEffect
{
    Discard = 0,
    Sequential = 1,
    FlipSequential = 3,
    FlipDiscard = 4,
}

public enum DxgiAlphaMode
{
    Unspecified = 0,
    Premultiplied = 1,
    Straight = 2,
    Ignore = 3,
}

[Flags]
public enum DxgiSwapChainFlags : uint
{
    None = 0,
    FrameLatencyWaitableObject = 0x40,
    AllowTearing = 0x800,
}

/// <summary>Blittable mirror of <c>DXGI_SWAP_CHAIN_DESC1</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct DxgiSwapChainDesc1
{
    public uint Width;
    public uint Height;
    public DxgiFormat Format;
    public int Stereo;
    public uint SampleCount;
    public uint SampleQuality;
    public uint BufferUsage;
    public uint BufferCount;
    public DxgiScaling Scaling;
    public DxgiSwapEffect SwapEffect;
    public DxgiAlphaMode AlphaMode;
    public DxgiSwapChainFlags Flags;
}

/// <summary>Outcome of a present call. Expected conditions are returned, not thrown.</summary>
public enum PresentResult
{
    Ok,
    Occluded,
    DeviceLost,
}

/// <summary>Back-buffer size in physical pixels.</summary>
public readonly record struct SwapChainTargetSize(uint Width, uint Height);

public enum D3D11QueryType : uint
{
    Timestamp = 2,
    TimestampDisjoint = 3,
}

/// <summary>Blittable mirror of <c>D3D11_QUERY_DESC</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct D3D11QueryDesc
{
    public D3D11QueryType Query;
    public uint MiscFlags;
}

/// <summary>Blittable mirror of <c>D3D11_QUERY_DATA_TIMESTAMP_DISJOINT</c> (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
public struct D3D11QueryDataTimestampDisjoint
{
    public ulong Frequency;
    public int Disjoint;
}

public enum D3D11Usage : uint
{
    Default = 0,
    Immutable = 1,
    Dynamic = 2,
    Staging = 3,
}

[Flags]
public enum D3D11CpuAccessFlags : uint
{
    None = 0,
    Write = 0x10000,
    Read = 0x20000,
}

public enum D3D11Map : uint
{
    Read = 1,
    Write = 2,
    ReadWrite = 3,
}

/// <summary>Blittable mirror of <c>D3D11_TEXTURE2D_DESC</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct D3D11Texture2DDesc
{
    public uint Width;
    public uint Height;
    public uint MipLevels;
    public uint ArraySize;
    public DxgiFormat Format;
    public uint SampleCount;
    public uint SampleQuality;
    public D3D11Usage Usage;
    public uint BindFlags;
    public D3D11CpuAccessFlags CpuAccessFlags;
    public uint MiscFlags;
}

/// <summary>Blittable mirror of <c>D3D11_MAPPED_SUBRESOURCE</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct D3D11MappedSubresource
{
    public nint Data;
    public uint RowPitch;
    public uint DepthPitch;
}

/// <summary>Tightly-packed top-down BGRA8 back-buffer snapshot read from D3D11 on demand.</summary>
public sealed record CapturedVideoFrame(int Width, int Height, byte[] BgraPixels);
