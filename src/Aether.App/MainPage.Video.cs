using Aether.Core.Interop.Mpv;
using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Video;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Storage.Pickers;
using Windows.System;

namespace Aether_App;

/// <summary>
/// The video flyout (screen 9.3) behind Video in the right-click menu: zoom, rotation, aspect ratio and colours. The
/// choices are for the open file: a new file goes back to Settings → Video, and an engine restart puts them back. The
/// Look is the exception: it is saved as last used and stays on across files.
/// Changes reach mpv on a pool thread, newest first, so dragging a slider never queues up. UI thread only, apart from
/// the pool work it starts.
/// </summary>
public sealed partial class MainPage
{
    // What the user picked for the open file; null while nothing differs from Settings → Video.
    private VideoAdjustments? _video;
    private bool _showVideoAfterMenu;
    private bool _videoPushing;
    private bool _videoStale; // the state changed again while a push was running

    private readonly LookLibrary _looks = new(LookLibrary.DefaultBuiltInDirectory, LookLibrary.DefaultUserDirectory);
    private bool _lookApplied; // mpv currently has the Look's filters and the copy-back decoder
    private bool _lookPushing;
    private bool _lookStale;

    private VideoLook CurrentLook => VideoLook.FromSettings(App.Settings.Current);

    private void InitializeVideo()
    {
        // Light dismiss would otherwise swallow the first click on the pill, as for the volume flyout.
        VideoFlyout.OverlayInputPassThroughElement = Osd;
        VideoPanel.Adjusted += OnVideoAdjusted;
        VideoPanel.Library = _looks;
        VideoPanel.LookChanged += (_, look) => ApplyLook(look);
        VideoPanel.AddLookRequested += (_, _) => _ = AddLookAsync();
        VideoPanel.OpenLooksFolderRequested += (_, _) => _ = OpenLooksFolderAsync();
    }

    /// <summary>The menu closes after this click, and its Closed handler opens the flyout, so the two holds do not cross.</summary>
    private void OnVideoMenuClick(object sender, RoutedEventArgs e) => _showVideoAfterMenu = true;

    private void ShowVideoIfRequested()
    {
        if (_showVideoAfterMenu)
        {
            _showVideoAfterMenu = false;
            ShowVideo();
        }
    }

    private void ShowVideo()
    {
        if (_session is null || XamlRoot is null)
        {
            return;
        }
        VideoAdjustments baseline = VideoAdjustments.FromSettings(App.Settings.Current);
        VideoPanel.Baseline = baseline;
        VideoPanel.Show(_video ?? baseline);
        VideoPanel.ShowLook(FallBackFromMissingGrade(CurrentLook));
        // The window less 8 either side (9.5), and the 1 px stroke the presenter adds.
        VideoPanel.MaxPanelHeight = Math.Max(0, XamlRoot.Size.Height - (double)Application.Current.Resources["AetherSizeMenuMaxHeightInset"] - 2);
        VideoFlyout.ShowAt(Osd, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedRight, ShowMode = FlyoutShowMode.Standard });
    }

    private void OnVideoFlyoutOpened(object? sender, object e) => HoldForMenu(true);

    private void OnVideoFlyoutClosed(object? sender, object e)
    {
        HoldForMenu(false);
        Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
    }

    /// <summary>The open video's size as shown, with the flyout's rotation applied; 0 by 0 without video.</summary>
    private (int Width, int Height) DisplayedVideoSize()
    {
        int width = _session?.ViewModel.VideoWidth ?? 0;
        int height = _session?.ViewModel.VideoHeight ?? 0;
        return VideoAdjustments.DisplaySize(width, height, _video?.Rotation ?? 0);
    }

    private void OnVideoAdjusted(object? sender, VideoAdjustments adjustments)
    {
        _video = adjustments == VideoAdjustments.FromSettings(App.Settings.Current) ? null : adjustments;
        // A quarter turn changes the picture's shape, so the surface and the window's aspect lock follow it.
        FitVideo();
        QueueWindowSync();
        PushVideo();
    }

    /// <summary>A new file starts from Settings → Video; after an engine restart the new engine gets the choices back.</summary>
    private void ApplyVideoForOpen(bool afterRestart)
    {
        PushLook();
        if (_video is null)
        {
            return;
        }
        if (!afterRestart)
        {
            _video = null;
            FitVideo(); // the new file starts unrotated
            QueueWindowSync();
        }
        PushVideo();
    }

    /// <summary>Sends the whole state to mpv. Only one push runs at a time; a change made meanwhile is sent when it ends.</summary>
    private async void PushVideo()
    {
        _videoStale = true;
        if (_videoPushing)
        {
            return;
        }
        _videoPushing = true;
        try
        {
            while (_videoStale && _isLoaded && _session is { } session)
            {
                _videoStale = false;
                IReadOnlyList<KeyValuePair<string, string>> properties =
                    (_video ?? VideoAdjustments.FromSettings(App.Settings.Current)).Properties();
                await Task.Run(() =>
                {
                    foreach ((string name, string value) in properties)
                    {
                        try
                        {
                            session.Context.SetProperty(name, value);
                        }
                        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
                        {
                            // The core refused, or is shutting down; the rest still apply.
                        }
                    }
                });
            }
        }
        finally
        {
            _videoPushing = false;
        }
    }

    /// <summary>A look file that has gone from the folder falls back to None, and the choice is saved that way.</summary>
    private VideoLook FallBackFromMissingGrade(VideoLook look)
    {
        if (look.Grade == VideoLook.NoGrade || _looks.ResolvePath(look.Grade) is not null)
        {
            return look;
        }
        VideoLook fallback = look with { Grade = VideoLook.NoGrade };
        ApplyLook(fallback);
        return fallback;
    }

    private void ApplyLook(VideoLook look)
    {
        look = look.Clamped();
        App.Settings.Set(SettingKeys.LookGrade, look.Grade);
        App.Settings.Set(SettingKeys.LookSharpen, look.Sharpen);
        App.Settings.Set(SettingKeys.LookGrain, look.Grain);
        _ = Task.Run(() =>
        {
            try
            {
                App.Settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Remembered for this session at worst.
            }
        });
        PushLook();
    }

    /// <summary>
    /// Sends the Look's filter to mpv. The filters run on the CPU, so a hardware decoder is switched to its copy-back form
    /// while any is on and back to the user's choice when the Look is off. With the Look off from the start, mpv is never touched.
    /// </summary>
    private async void PushLook()
    {
        _lookStale = true;
        if (_lookPushing)
        {
            return;
        }
        _lookPushing = true;
        try
        {
            while (_lookStale && _isLoaded && _session is { } session)
            {
                _lookStale = false;
                VideoLook look = CurrentLook;
                string filter = look.MpvFilter(_looks.ResolvePath(look.Grade));
                bool active = filter.Length > 0;
                if (!active && !_lookApplied)
                {
                    continue;
                }
                bool switchDecoder = active != _lookApplied;
                string decoder = VideoLook.HardwareDecoder(App.Settings.Current.HardwareDecoding, active);
                if (switchDecoder)
                {
                    _lastDecoder = null; // the decoder is reopened, which must not read as a hardware failure
                }
                await Task.Run(() =>
                {
                    try
                    {
                        if (switchDecoder)
                        {
                            session.Context.SetProperty("hwdec", decoder);
                        }
                        if (active)
                        {
                            session.Context.SetProperty("vf", filter);
                        }
                        else
                        {
                            session.Context.Command("vf", "clear");
                        }
                    }
                    catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
                    {
                        // The core refused the filter, or is shutting down; playback carries on unfiltered.
                    }
                });
                _lookApplied = active;
            }
        }
        finally
        {
            _lookPushing = false;
        }
    }

    private async Task AddLookAsync()
    {
        if (_host is not Window window)
        {
            return;
        }
        VideoFlyout.Hide(); // the picker takes focus, which would close it anyway
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".cube");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        _chrome.SetHold(ChromeHolds.Interaction, true);
        ApplyChrome();
        Windows.Storage.StorageFile? file;
        try
        {
            file = await picker.PickSingleFileAsync();
        }
        finally
        {
            _chrome.SetHold(ChromeHolds.Interaction, false);
            ApplyChrome();
        }
        if (file is not null)
        {
            if (_looks.TryImport(file.Path) is { } grade)
            {
                ApplyLook(CurrentLook with { Grade = grade });
            }
            else
            {
                _toasts?.Show("look-invalid", AppStrings.Get("Toast_LookInvalid"), ToastTone.Failure);
            }
        }
        ShowVideo();
    }

    private async Task OpenLooksFolderAsync()
    {
        try
        {
            Directory.CreateDirectory(_looks.UserDirectory);
            await Launcher.LaunchFolderPathAsync(_looks.UserDirectory);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to show; the folder is made again the next time a look is added.
        }
    }
}
