using System.Diagnostics;
using Aether.Core.Interop.DirectX;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT;

namespace Aether_App.Controls;

/// <summary>
/// Video surface: a <see cref="SwapChainPanel"/> that hosts a flip-model swap chain
/// (<see cref="DxgiPresenter"/>) and drives it from a dedicated render thread.
/// <para>
/// Threading: the UI thread creates/attaches/detaches the swap chain and posts size changes
/// with <see cref="DxgiPresenter.SetTargetSize"/> (non-blocking). The "Aether.Render" thread
/// is the single owner of <c>WaitForNextFrame</c>, <c>Clear</c>, <c>Present</c> and
/// <c>ApplyPendingResize</c>; anything else that renders into <see cref="Presenter"/> must do so
/// from that thread. <see cref="Presenter"/> is null while the control is unloaded.
/// </para>
/// </summary>
public sealed partial class MpvVideoSurface : SwapChainPanel
{
    private D3D11DeviceManager? _device;
    private RenderLoop? _loop;

    public MpvVideoSurface()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
        CompositionScaleChanged += OnCompositionScaleChanged;
    }

    /// <summary>The live presenter, or null while unloaded. See the class remarks for the threading contract.</summary>
    public DxgiPresenter? Presenter { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Presenter is not null)
        {
            return; // Loaded can fire again without an Unloaded in between
        }

        D3D11DeviceManager? device = null;
        DxgiPresenter? presenter = null;
        try
        {
            device = D3D11DeviceManager.Create();
            var size = DxgiPresenter.ComputeTargetSize(
                ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);
            presenter = new DxgiPresenter(device, new DxgiPresenterOptions { Width = size.Width, Height = size.Height });

            SwapChainPanelNative.SetSwapChain(PanelPointer, presenter.SwapChainPointer);
            GC.KeepAlive(this);
        }
        catch
        {
            presenter?.Dispose();
            device?.Dispose();
            throw;
        }

        _device = device;
        Presenter = presenter;

        // Queue the scale too: the swap chain starts at 1:1 and the first frame applies the inverse transform.
        presenter.SetTargetSize(ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);

        _loop = new RenderLoop(presenter);
        _loop.Start();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        RenderLoop? loop = _loop;
        DxgiPresenter? presenter = Presenter;
        D3D11DeviceManager? device = _device;
        if (presenter is null)
        {
            return; // Unloaded before Loaded finished, or twice
        }

        _loop = null;
        Presenter = null;
        _device = null;

        // Stop and join first: the thread must be off the swap chain before it is detached and released.
        bool stopped = loop?.Stop(500) ?? true;
        SwapChainPanelNative.SetSwapChain(PanelPointer, 0);
        GC.KeepAlive(this);

        if (!stopped)
        {
            // A stuck thread still uses the presenter; disposing now would free it underneath.
            // The thread's reference keeps both alive and their finalizers release them once it exits.
            Debug.WriteLine("MpvVideoSurface: render thread did not stop in time; presenter left to the finalizer.");
            return;
        }

        presenter.Dispose();
        device?.Dispose();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => PostSize();

    private void OnCompositionScaleChanged(SwapChainPanel sender, object args) => PostSize();

    private void PostSize() =>
        Presenter?.SetTargetSize(ActualWidth, ActualHeight, CompositionScaleX, CompositionScaleY);

    private nint PanelPointer => ((IWinRTObject)this).NativeObject.ThisPtr;

    /// <summary>One render thread and its stop flag; a new instance per load keeps stale threads isolated.</summary>
    private sealed class RenderLoop(DxgiPresenter presenter)
    {
        private volatile bool _stop;
        private readonly Thread _thread = new(static state => ((RenderLoop)state!).Run()) { IsBackground = true, Name = "Aether.Render" };

        public void Start() => _thread.Start(this);

        /// <summary>Signals stop and waits up to <paramref name="timeoutMs"/>; false if the thread is still running.</summary>
        public bool Stop(int timeoutMs)
        {
            _stop = true;
            return _thread.Join(timeoutMs);
        }

        private void Run()
        {
            try
            {
                while (!_stop)
                {
                    // 100 ms timeout so a stop request is noticed even if the swap chain stalls.
                    if (!presenter.WaitForNextFrame(100))
                    {
                        continue;
                    }

                    presenter.ApplyPendingResize();
                    presenter.Clear(0f, 0f, 0f, 1f);
                    if (presenter.Present() == PresentResult.DeviceLost)
                    {
                        Debug.WriteLine("MpvVideoSurface: device lost; render loop stopped (recovery is Pass 2).");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MpvVideoSurface: render loop failed: {ex}");
            }
        }
    }
}
