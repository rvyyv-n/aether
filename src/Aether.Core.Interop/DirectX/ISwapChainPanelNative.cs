using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Aether.Core.Interop.DirectX;

[GeneratedComInterface]
[Guid("63AAD0B8-7C24-40FF-85A8-640D944CC325")]
public partial interface ISwapChainPanelNative
{
    [PreserveSig]
    int SetSwapChain(IntPtr swapChain);
}
