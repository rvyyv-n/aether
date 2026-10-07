using System;
using System.Threading;
using Aether.Core.Interop.DirectX.Native;

namespace Aether.Core.Interop.Rendering;

/// <summary>
/// A swap chain on its way from the render thread to the UI thread. Holds its own COM reference,
/// so the pointer stays valid even if the render thread replaces the swap chain (device loss)
/// before the UI thread gets to it. Bind with <see cref="DirectX.SwapChainPanelNative.SetSwapChain"/>,
/// then dispose.
/// </summary>
public sealed class SwapChainHandoff : IDisposable
{
    private nint _swapChain;

    internal SwapChainHandoff(nint swapChain, int generation)
    {
        Com.AddRef(swapChain);
        _swapChain = swapChain;
        Generation = generation;
    }

    ~SwapChainHandoff() => Com.Release(ref _swapChain);

    /// <summary><c>IDXGISwapChain1*</c> (borrowed from this handoff); 0 after <see cref="Dispose"/>.</summary>
    public nint SwapChain => Volatile.Read(ref _swapChain);

    /// <summary>Device generation, starting at 1 and incremented on every recovery. Bind only the newest.</summary>
    public int Generation { get; }

    public void Dispose()
    {
        Com.Release(ref _swapChain);
        GC.SuppressFinalize(this);
    }
}

/// <summary>Callbacks from <see cref="MpvRenderLoop"/>. All run on the render thread and must not block on the UI thread.</summary>
public interface IRenderLoopHost
{
    /// <summary>
    /// A new swap chain exists (start, or recovery after device loss). Post it to the UI thread
    /// (<c>DispatcherQueue.TryEnqueue</c>), call <c>SetSwapChain</c> there and dispose the handoff.
    /// Ownership of <paramref name="handoff"/> passes to the host.
    /// </summary>
    void OnSwapChainCreated(SwapChainHandoff handoff);

    /// <summary>The device was lost; recovery starts immediately. <paramref name="reason"/> is the HRESULT.</summary>
    void OnDeviceLost(int reason, int generation);

    /// <summary>A frame, creation or teardown step threw. The loop tears down and retries with backoff.</summary>
    void OnFault(Exception exception);
}
