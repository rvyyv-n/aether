using System;
using Aether.Core.Interop.DirectX.Native;

namespace Aether.Core.Interop.DirectX;

/// <summary>
/// Bridge to <c>ISwapChainPanelNative</c> (microsoft.ui.xaml.media.dxinterop.h) without a
/// managed COM wrapper. Call on the panel's UI thread.
/// </summary>
public static unsafe class SwapChainPanelNative
{
    /// <summary>
    /// Binds <paramref name="swapChain"/> (an <c>IDXGISwapChain1*</c>, or 0 to detach) to the panel.
    /// </summary>
    /// <param name="panelUnknown">
    /// Any interface pointer of the WinUI <c>SwapChainPanel</c>, e.g.
    /// <c>((WinRT.IWinRTObject)panel).NativeObject.ThisPtr</c>. Borrowed; not released here.
    /// </param>
    public static void SetSwapChain(nint panelUnknown, nint swapChain)
    {
        if (panelUnknown == 0)
        {
            throw new ArgumentNullException(nameof(panelUnknown));
        }

        Com.ThrowIfFailed(Com.QueryInterface(panelUnknown, DxNative.IID_ISwapChainPanelNative, out nint native));
        try
        {
            // ISwapChainPanelNative::SetSwapChain; the panel takes its own reference.
            Com.ThrowIfFailed(((delegate* unmanaged[Stdcall]<nint, nint, int>)Com.Vtbl(native)[3])(native, swapChain));
        }
        finally
        {
            Com.Release(native);
        }
    }
}
