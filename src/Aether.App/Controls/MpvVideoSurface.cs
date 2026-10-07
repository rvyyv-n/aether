using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Rendering;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Aether_App.Controls;

/// <summary>
/// Video surface: a <see cref="SwapChainPanel"/> showing the swap chain of an <see cref="MpvRenderLoop"/>.
/// <para>
/// Threading: the render thread ("Aether.Render") owns the device, the swap chain and whatever draws
/// into it, and recreates all of them after device loss. This control only posts size changes
/// (non-blocking) and binds each new swap chain to the panel on the UI thread. Nothing here waits on
/// the render thread.
/// </para>
/// </summary>
public sealed partial class MpvVideoSurface : SwapChainPanel
{
    private MpvRenderLoop? _loop;
    private int _boundGeneration;

    public MpvVideoSurface()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        CompositionScaleChanged += OnCompositionScaleChanged;
    }

    /// <summary>
    /// What draws the frames, read when the control loads (e.g. <see cref="AngleMpvRenderBackendFactory"/>
    /// for video). Null clears to black.
    /// </summary>
    public IVideoRenderBackendFactory? BackendFactory { get; set; }

    /// <summary>The device was lost; the render thread is already recreating it. Raised on the UI thread with the HRESULT.</summary>
    public event EventHandler<int>? DeviceLost;

    /// <summary>A render-thread step failed; the loop retries on its own. Raised on the UI thread.</summary>
    public event EventHandler<Exception>? RenderFault;

    /// <summary>The running loop, or null while unloaded.</summary>
    public MpvRenderLoop? RenderLoop => _loop;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loop is not null)
        {
            return; // Loaded can fire again without an Unloaded in between
        }

        var host = new Host(this, DispatcherQueue);
        var loop = new MpvRenderLoop(BackendFactory, host);
        host.Loop = loop; // published to the render thread by Start
        _loop = loop;
        _boundGeneration = 0;
        loop.SetTargetSize(ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);
        loop.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MpvRenderLoop? loop = _loop;
        if (loop is null)
        {
            return; // Unloaded before Loaded, or twice
        }
        _loop = null;

        // Non-blocking: the render thread releases device, swap chain and backend as it exits.
        // The panel holds its own reference to the bound swap chain until it is detached here.
        loop.Dispose();
        SwapChainPanelNative.SetSwapChain(PanelPointer, 0);
        GC.KeepAlive(this);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => PostSize();

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args) => PostSize();

    private void PostSize() =>
        _loop?.SetTargetSize(ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);

    private nint PanelPointer => ((IWinRTObject)this).NativeObject.ThisPtr;

    /// <summary>UI thread: binds a swap chain unless the loop was replaced or a newer one is already bound.</summary>
    private void Bind(MpvRenderLoop loop, SwapChainHandoff handoff)
    {
        using (handoff)
        {
            if (!ReferenceEquals(loop, _loop) || handoff.Generation <= _boundGeneration)
            {
                return;
            }
            SwapChainPanelNative.SetSwapChain(PanelPointer, handoff.SwapChain);
            GC.KeepAlive(this);
            _boundGeneration = handoff.Generation;
        }
    }

    /// <summary>Render-thread callbacks, forwarded to the UI thread with <c>TryEnqueue</c> (never blocks).</summary>
    private sealed class Host(MpvVideoSurface surface, DispatcherQueue dispatcher) : IRenderLoopHost
    {
        public MpvRenderLoop? Loop { get; set; }

        public void OnSwapChainCreated(SwapChainHandoff handoff)
        {
            MpvRenderLoop? loop = Loop;
            if (loop is null || !dispatcher.TryEnqueue(() => surface.Bind(loop, handoff)))
            {
                handoff.Dispose();
            }
        }

        public void OnDeviceLost(int reason, int generation) =>
            dispatcher.TryEnqueue(() => surface.DeviceLost?.Invoke(surface, reason));

        public void OnFault(Exception exception) =>
            dispatcher.TryEnqueue(() => surface.RenderFault?.Invoke(surface, exception));
    }
}
