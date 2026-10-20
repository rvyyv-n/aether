using System.Net;
using System.Net.NetworkInformation;
using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Server.LanHost;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;

namespace Aether_App;

/// <summary>
/// The phone pairing card (11.3): a switch that starts and stops the stream server for the open file, with its QR
/// code, address and how many phones are watching. The server is off until switched on here, and stops when the
/// switch goes off, another file or link opens, or the page unloads at exit. <see cref="LanStreamPanel"/> decides
/// what the card shows. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private const int LanStreamPort = 8443;

    // Stopping runs in the background so the window never waits on it; Program gives it a moment at exit.
    private static Task s_lanServerStopping = Task.CompletedTask;

    private KestrelStreamServer? _lanServer;
    private Task _lanServerStarted = Task.CompletedTask;
    private string? _lanServedPath;
    private LanServerState _lanState;
    private int _lanStartVersion;
    // Kept between openings, so a change while the card was closed is still noticed.
    private IPAddress? _lanAddress;
    private bool _lanAddressChanged;
    private int _lanAddressVersion;
    private string? _lanCodeUrl;
    private double _lanCodeScale;
    private bool _lanShown;
    private bool _lanRendering;
    private DispatcherQueueTimer? _lanWatchersTimer;
    private DispatcherQueueTimer? _lanCopiedTimer;

    /// <summary>Waits up to <paramref name="timeoutMilliseconds"/> for a stopping server to let go of its port.</summary>
    internal static void WaitForLanServerStop(int timeoutMilliseconds) => s_lanServerStopping.Wait(timeoutMilliseconds);

    private void InitializeLanStream()
    {
        LanStreamTitle.Text = AppStrings.Get("LanStream_Title");
        AutomationProperties.SetName(LanStreamSwitch, LanStreamTitle.Text);
        LanStreamCodeOffText.Text = AppStrings.Get("LanStream_CodeOff");
        AutomationProperties.SetName(LanStreamCode, AppStrings.Get("LanStream_CodeName"));
        AutomationProperties.SetName(LanStreamAddress, AppStrings.Get("LanStream_AddressName"));
        LanStreamCopyLabel.Text = AppStrings.Get("LanStream_Copy");
        LanStreamHint.Text = AppStrings.Get("LanStream_Hint");

        // The count is the only thing that changes on its own, and only matters while someone is looking.
        _lanWatchersTimer = DispatcherQueue.CreateTimer();
        _lanWatchersTimer.Interval = TimeSpan.FromSeconds(2);
        _lanWatchersTimer.Tick += (_, _) => RenderLanStream();
        _lanCopiedTimer = DispatcherQueue.CreateTimer();
        _lanCopiedTimer.Interval = TimeSpan.FromSeconds(2);
        _lanCopiedTimer.IsRepeating = false;
        _lanCopiedTimer.Tick += (_, _) => LanStreamCopyLabel.Text = AppStrings.Get("LanStream_Copy");
    }

    private void OnLanStreamAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CloseLanStream())
        {
            args.Handled = true;
        }
        else if (!ArePlayerKeysPaused)
        {
            args.Handled = true;
            ShowLanStream();
        }
    }

    private void OnStreamMenuClick(object sender, RoutedEventArgs e) => ShowLanStream();

    private void OnRailStreamClick(object sender, RoutedEventArgs e)
    {
        CloseRail();
        ShowLanStream();
    }

    private void ShowLanStream()
    {
        if (_lanShown || IsOpenUrlShown || _settingsShown)
        {
            return;
        }
        EndTour();
        CloseRail();
        LeavePictureInPicture();
        _lanShown = true;
        if (_lanState is LanServerState.PortUnavailable or LanServerState.Failed)
        {
            _lanState = LanServerState.Off; // an old failure is not news; switching on again says if it still fails
        }
        // Read at once rather than in the background, so the card never opens on a wrong "no network".
        ApplyLanAddress(LanAddressPicker.PickFromSystem());
        NetworkChange.NetworkAddressChanged += OnLanNetworkChanged;
        if (_fader is null)
        {
            LanStreamOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            // The scrim only fades; the card rises with it, as Open URL does.
            _fader.Enter(LanStreamOverlay, null);
            _fader.Slide(LanStreamCard, true, ChromeFader.FromBelow);
        }
        _chrome.SetHold(ChromeHolds.Dialog, true);
        ApplyChrome();
        RenderLanStream();
        FocusLanStreamCard();
    }

    // A disabled switch cannot take focus, which would leave it on the page behind the scrim.
    private void FocusLanStreamCard() =>
        (LanStreamSwitch.IsEnabled ? (UIElement)LanStreamSwitch : LanStreamStatusText).Focus(FocusState.Programmatic);

    /// <summary>Esc, the shortcut and a click outside close it. True if it was open.</summary>
    private bool CloseLanStream()
    {
        if (!_lanShown)
        {
            return false;
        }
        _lanShown = false;
        NetworkChange.NetworkAddressChanged -= OnLanNetworkChanged;
        _lanAddressVersion++; // a background read still running has nowhere to show
        SyncLanWatchersTimer();
        if (_fader is null)
        {
            LanStreamOverlay.Visibility = Visibility.Collapsed;
        }
        else
        {
            _fader.Exit(LanStreamOverlay, null);
            _fader.Slide(LanStreamCard, false, ChromeFader.FromBelow);
        }
        _chrome.SetHold(ChromeHolds.Dialog, false);
        ApplyChrome();
        Focus(FocusState.Programmatic); // playback keys back on the page
        return true;
    }

    private void OnLanStreamOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, LanStreamOverlay))
        {
            e.Handled = true;
            CloseLanStream();
        }
    }

    private void OnLanStreamToggled(object sender, RoutedEventArgs e)
    {
        if (_lanRendering)
        {
            return;
        }
        if (LanStreamSwitch.IsOn)
        {
            StartLanServer();
        }
        else
        {
            StopLanServer();
        }
    }

    private async void StartLanServer()
    {
        if (_lanServer is not null || LanSource() != LanStreamSource.File || _currentPath is not { } path)
        {
            RenderLanStream();
            return;
        }
        var server = new KestrelStreamServer(LanStreamPort);
        server.SetActiveFile(path);
        _lanServer = server;
        _lanServedPath = path;
        _lanState = LanServerState.Starting;
        _lanAddressChanged = false;
        int version = ++_lanStartVersion;
        RenderLanStream();

        // Building the web host takes long enough to stutter the window, so it starts off the UI thread.
        Task started = Task.Run(() => server.StartAsync());
        _lanServerStarted = started;
        LanServerState result;
        try
        {
            await started;
            result = LanServerState.On;
        }
        catch (Exception ex)
        {
            result = KestrelStreamServer.IsPortUnavailable(ex) ? LanServerState.PortUnavailable : LanServerState.Failed;
        }
        if (version != _lanStartVersion)
        {
            return; // switched off, or another file opened, while it started; that stop disposes this server
        }
        if (result != LanServerState.On)
        {
            ReleaseLanServer();
        }
        _lanState = result;
        RenderLanStream();
    }

    private void StopLanServer()
    {
        _lanStartVersion++;
        ReleaseLanServer();
        _lanState = LanServerState.Off;
        _lanAddressChanged = false;
        RenderLanStream();
    }

    /// <summary>On unload: the server never outlives the window.</summary>
    private void ShutDownLanStream()
    {
        StopLanServer();
        NetworkChange.NetworkAddressChanged -= OnLanNetworkChanged;
        _lanWatchersTimer?.Stop();
        _lanCopiedTimer?.Stop();
    }

    private void ReleaseLanServer()
    {
        if (_lanServer is not { } server)
        {
            return;
        }
        Task started = _lanServerStarted;
        _lanServer = null;
        _lanServedPath = null;
        _lanCodeUrl = null;
        Task previous = s_lanServerStopping;
        s_lanServerStopping = Task.Run(async () =>
        {
            try
            {
                await started; // disposing mid-start would miss the host it is about to create
            }
            catch (Exception)
            {
                // It failed to start; there may still be parts to let go of.
            }
            try
            {
                await server.DisposeAsync();
            }
            catch (Exception)
            {
                // Nothing is left to do for a server that will not stop cleanly; the process ends soon enough.
            }
            await previous;
        });
    }

    /// <summary>Called as a file or link opens: the phone keeps the old file only if it is the same one reopening.</summary>
    private void SyncLanStreamSource(string path)
    {
        if (_lanServer is not null && !string.Equals(_lanServedPath, path, StringComparison.OrdinalIgnoreCase))
        {
            StopLanServer();
        }
    }

    private LanStreamSource LanSource()
    {
        if (_currentPath is not { } path || !(HasVideo || _session?.ViewModel.Status == PlaybackStatus.Loading))
        {
            return LanStreamSource.Nothing;
        }
        return _currentWebLink is null && Path.IsPathFullyQualified(path) ? LanStreamSource.File : LanStreamSource.Link;
    }

    // Raised on a thread-pool thread whenever any adapter gains or loses an address.
    private void OnLanNetworkChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(async () =>
    {
        if (!_lanShown)
        {
            return;
        }
        int version = ++_lanAddressVersion;
        IPAddress? found = await Task.Run(LanAddressPicker.PickFromSystem);
        if (_lanShown && version == _lanAddressVersion)
        {
            ApplyLanAddress(found);
            RenderLanStream();
        }
    });

    private void ApplyLanAddress(IPAddress? found)
    {
        if (_lanState == LanServerState.On && _lanAddress is not null && found is not null && !found.Equals(_lanAddress))
        {
            _lanAddressChanged = true;
        }
        _lanAddress = found;
    }

    private void RenderLanStream()
    {
        if (_lanServer is not null && LanSource() == LanStreamSource.Nothing)
        {
            StopLanServer(); // the file closed; renders again itself
            return;
        }
        if (!_lanShown)
        {
            SyncLanWatchersTimer();
            return;
        }
        var panel = new LanStreamPanel(
            LanSource(), _lanState, _lanAddress is not null, _lanServer?.WatcherCount ?? 0, _lanAddressChanged);

        _lanRendering = true;
        LanStreamSwitch.IsOn = panel.SwitchOn;
        LanStreamSwitch.IsEnabled = panel.SwitchEnabled;
        _lanRendering = false;

        SetLiveText(LanStreamStatusText, AppStrings.Format(panel.StatusKey, panel.Watchers));
        LanStreamStatusDot.Fill = ToneBrush(LanStreamStatusDot, panel.StatusTone == LanStreamTone.Ready ? Tone.Success : Tone.Neutral);

        if (panel.ShowsCode && _lanServer is { } server && _lanAddress is { } address)
        {
            string url = server.GetMobileWatchUrl(address.ToString());
            ShowLanCode(url);
            LanStreamAddress.Text = url;
            ToolTipService.SetToolTip(LanStreamAddress, url);
        }
        Visibility code = panel.ShowsCode ? Visibility.Visible : Visibility.Collapsed;
        LanStreamCodeTile.Visibility = code;
        LanStreamAddressRow.Visibility = code;
        LanStreamCodeOff.Visibility = panel.ShowsCode ? Visibility.Collapsed : Visibility.Visible;

        LanStreamNote.Visibility = panel.NoteKey is null ? Visibility.Collapsed : Visibility.Visible;
        LanStreamNoteDot.Visibility = panel.NoteIsWarning ? Visibility.Visible : Visibility.Collapsed;
        SetLiveText(LanStreamNoteText, panel.NoteKey is { } note ? AppStrings.Format(note, LanStreamPort) : string.Empty);
        SyncLanWatchersTimer();
    }

    // Setting the text alone does not make Narrator speak a live region; the event does, so it fires only on a real change.
    private static void SetLiveText(TextBlock block, string text)
    {
        if (block.Text == text)
        {
            return;
        }
        block.Text = text;
        if (text.Length > 0)
        {
            var peer = FrameworkElementAutomationPeer.FromElement(block) ?? FrameworkElementAutomationPeer.CreatePeerForElement(block);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void SyncLanWatchersTimer()
    {
        if (_lanShown && _lanState == LanServerState.On)
        {
            if (_lanWatchersTimer is { IsRunning: false } timer)
            {
                timer.Start();
            }
        }
        else
        {
            _lanWatchersTimer?.Stop();
        }
    }

    /// <summary>
    /// Draws the code at a whole number of screen pixels per module and shows it at exactly that size, so no module is
    /// smeared across two pixels. Drawn again only when the address or the display scale changes.
    /// </summary>
    private async void ShowLanCode(string url)
    {
        double scale = XamlRoot?.RasterizationScale ?? 1;
        if (url == _lanCodeUrl && scale == _lanCodeScale)
        {
            return;
        }
        _lanCodeUrl = url;
        _lanCodeScale = scale;
        double tile = (double)Application.Current.Resources["AetherSizeLanCode"];
        (byte[] png, int pixels) = QrPairingService.CreatePngToFit(url, (int)(tile * scale));

        var bitmap = new BitmapImage();
        using (var stream = new InMemoryRandomAccessStream())
        {
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await bitmap.SetSourceAsync(stream);
        }
        if (url != _lanCodeUrl)
        {
            return; // the address changed while this one was decoding
        }
        LanStreamCode.Source = bitmap;
        LanStreamCode.Width = pixels / scale;
        LanStreamCode.Height = pixels / scale;
    }

    private void OnLanStreamCopyClick(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(LanStreamAddress.Text);
        try
        {
            Clipboard.SetContent(package);
            LanStreamCopyLabel.Text = AppStrings.Get("LanStream_Copied");
            _lanCopiedTimer?.Start(); // Start on a running timer restarts the 2 seconds
        }
        catch (Exception)
        {
            // Another app holds the clipboard open; the address stays selectable.
        }
    }

    private void ApplyLanStreamShortcutLabels()
    {
        CtxStream.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.ToggleLanStream);
        ToolTipService.SetToolTip(RailStream, Tip("Rail_Stream", ShortcutAction.ToggleLanStream));
    }
}
