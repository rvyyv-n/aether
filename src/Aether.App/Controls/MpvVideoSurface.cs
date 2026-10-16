using Aether.Core.Interop.DirectX;
using Aether.Core.Interop.Mpv;
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
    private readonly BackdropSource _backdrop = new();

    public MpvVideoSurface()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        CompositionScaleChanged += OnCompositionScaleChanged;
    }

    private MpvContext? _player;

    /// <summary>
    /// Overrides what draws the frames, read whenever a render loop starts. Null (the default) uses the
    /// attached player (<see cref="AttachPlayer"/>), or clears to black when none is attached.
    /// </summary>
    public IVideoRenderBackendFactory? BackendFactory { get; set; }

    /// <summary>The attached core, or null. The surface never disposes it.</summary>
    public MpvContext? Player => _player;

    /// <summary>
    /// UI thread: renders <paramref name="player"/> into this surface. The caller keeps ownership of the
    /// core and should detach before disposing it. Disposing while attached is memory-safe (the mpv
    /// render context holds its own reference, so the core lives until the render loop lets go), but
    /// after the next device loss the loop can no longer recreate the render context and keeps
    /// reporting <see cref="RenderFault"/>. If the surface is loaded, its render loop restarts (non-blocking).
    /// </summary>
    public void AttachPlayer(MpvContext player)
    {
        ArgumentNullException.ThrowIfNull(player);
        ObjectDisposedException.ThrowIf(player.IsDisposed, player);
        if (ReferenceEquals(player, _player))
        {
            return;
        }
        _player = player;
        RestartLoop();
    }

    /// <summary>UI thread: stops rendering the attached core (the surface clears to black). Does not dispose it.</summary>
    public void DetachPlayer()
    {
        if (_player is null)
        {
            return;
        }
        _player = null;
        RestartLoop();
    }

    /// <summary>The device was lost; the render thread is already recreating it. Raised on the UI thread with the HRESULT.</summary>
    public event EventHandler<int>? DeviceLost;

    /// <summary>A render-thread step failed; the loop retries on its own. Raised on the UI thread.</summary>
    public event EventHandler<Exception>? RenderFault;

    /// <summary>
    /// True once the attached player's render context exists (its loop has bound a swap chain).
    /// mpv sets up video output when a file loads, so a file loaded before this plays without a picture.
    /// </summary>
    public bool IsPlayerReady { get; private set; }

    /// <summary>Raised on the UI thread when <see cref="IsPlayerReady"/> turns true.</summary>
    public event EventHandler? PlayerReady;

    /// <summary>The running loop, or null while unloaded.</summary>
    public MpvRenderLoop? RenderLoop => _loop;

    /// <summary>
    /// Any thread. Where a card sits over the video (swap-chain pixels, relative to this
    /// surface) so the render thread can blur behind it; redraws even when playback is paused.
    /// </summary>
    public void PostBackdrop(BackdropSlot slot, in BackdropRegion region)
    {
        _backdrop.Post(slot, region);
        _loop?.RequestRedraw();
    }

    /// <summary>UI thread. Turns a card's blur on or off (off: no video, settings, picture in picture).</summary>
    public void SetBackdropActive(BackdropSlot slot, bool active)
    {
        _backdrop.SetActive(slot, active);
        _loop?.RequestRedraw();
    }

    /// <summary>UI thread. Fades a card's blur with the card; a zero duration is instant.</summary>
    public void FadeBackdrop(BackdropSlot slot, bool visible, double durationMilliseconds)
    {
        _backdrop.Fade(slot, visible, durationMilliseconds);
        _loop?.RequestRedraw();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loop is not null)
        {
            return; // Loaded can fire again without an Unloaded in between
        }
        StartLoop();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => StopLoop();

    /// <summary>
    /// Swaps in a loop for the current player. mpv allows one render context per core, so the old loop
    /// is given a moment to free its context before the new one starts; otherwise the new loop's first
    /// creation fails and waits out a retry delay.
    /// </summary>
    private void RestartLoop()
    {
        if (_loop is null)
        {
            return; // not loaded: OnLoaded picks the player up
        }
        StopLoop(waitForExit: true);
        StartLoop();
    }

    private void StartLoop()
    {
        IVideoRenderBackendFactory? factory = BackendFactory ?? (_player is { } player ? new AngleMpvRenderBackendFactory(player, _backdrop) : null);
        var host = new Host(this, DispatcherQueue);
        var loop = new MpvRenderLoop(factory, host, new MpvRenderLoopOptions { Backdrop = _backdrop });
        host.Loop = loop; // published to the render thread by Start
        _loop = loop;
        _boundGeneration = 0;
        IsPlayerReady = false;
        loop.SetTargetSize(ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);
        loop.Start();
    }

    private void StopLoop(bool waitForExit = false)
    {
        MpvRenderLoop? loop = _loop;
        if (loop is null)
        {
            return; // Unloaded before Loaded, or twice
        }
        _loop = null;
        IsPlayerReady = false;

        // The render thread releases device, swap chain and backend as it exits. Unloading never waits for it;
        // a restart waits briefly (teardown takes a few ms) so the replacement can take over the mpv core.
        // The panel holds its own reference to the bound swap chain until it is detached here.
        if (waitForExit)
        {
            loop.Stop(250);
        }
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
        // The loop creates its backend, and with it the mpv render context, before handing off a swap chain.
        if (!IsPlayerReady && _player is not null)
        {
            IsPlayerReady = true;
            PlayerReady?.Invoke(this, EventArgs.Empty);
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
