using System;
using System.Runtime.InteropServices;

namespace Aether.Core.Interop.DirectX.Native;

internal static unsafe partial class DxNative
{
    public const uint D3D11_SDK_VERSION = 7;

    public const uint DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x20;
    public const int DXGI_FEATURE_PRESENT_ALLOW_TEARING = 0;
    public const uint DXGI_PRESENT_ALLOW_TEARING = 0x200;

    public const int DXGI_STATUS_OCCLUDED = 0x087A0001;
    public const int DXGI_ERROR_DEVICE_REMOVED = unchecked((int)0x887A0005);
    public const int DXGI_ERROR_DEVICE_RESET = unchecked((int)0x887A0007);
    public const int DXGI_ERROR_DEVICE_HUNG = unchecked((int)0x887A0006);
    public const int DXGI_ERROR_DRIVER_INTERNAL_ERROR = unchecked((int)0x887A0020);
    public const int DXGI_ERROR_SDK_COMPONENT_MISSING = unchecked((int)0x887A002D);

    public const uint WAIT_OBJECT_0 = 0;
    public const uint WAIT_TIMEOUT = 0x102;
    public const uint WAIT_FAILED = 0xFFFFFFFF;

    public static readonly Guid IID_ID3D11Multithread = new(0x9B7E4E00, 0x342C, 0x4106, 0xA1, 0x9F, 0x4F, 0x27, 0x04, 0xF6, 0x89, 0xF0);
    public static readonly Guid IID_ID3D11Texture2D = new(0x6F15AAF2, 0xD208, 0x4E89, 0x9A, 0xB4, 0x48, 0x95, 0x35, 0xD3, 0x4F, 0x9C);
    public static readonly Guid IID_IDXGIDevice = new(0x54EC77FA, 0x1377, 0x44E6, 0x8C, 0x32, 0x88, 0xFD, 0x5F, 0x44, 0xC8, 0x4C);
    public static readonly Guid IID_IDXGIFactory2 = new(0x50C83A1C, 0xE072, 0x4C48, 0x87, 0xB0, 0x36, 0x30, 0xFA, 0x36, 0xA6, 0xD0);
    public static readonly Guid IID_IDXGIFactory5 = new(0x7632E1F5, 0xEE65, 0x4DCA, 0x87, 0xFD, 0x84, 0xCD, 0x75, 0xF8, 0x83, 0x8D);
    public static readonly Guid IID_IDXGISwapChain2 = new(0xA8BE2AC4, 0x199F, 0x4946, 0xB3, 0x31, 0x79, 0x59, 0x9F, 0xB9, 0x8D, 0xE7);
    public static readonly Guid IID_ISwapChainPanelNative = new(0x63AAD0B8, 0x7C24, 0x40FF, 0x85, 0xA8, 0x64, 0x0D, 0x94, 0x4C, 0xC3, 0x25);

    [LibraryImport("d3d11.dll")]
    public static partial int D3D11CreateDevice(
        nint pAdapter,
        D3DDriverType driverType,
        nint software,
        D3D11CreateDeviceFlags flags,
        D3DFeatureLevel* pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        nint* ppDevice,
        D3DFeatureLevel* pFeatureLevel,
        nint* ppImmediateContext);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObjectEx(SafeHandle handle, uint milliseconds, int alertable);
}
