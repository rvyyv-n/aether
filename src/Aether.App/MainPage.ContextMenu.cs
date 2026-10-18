using Aether.Core.Playback;
using Aether.Core.Playback.Input;
using Aether.Core.Playback.Recents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Aether_App;

/// <summary>
/// The right-click menu (screen 9.1). Audio and Subtitles open the tracks flyout (MainPage.Tracks.cs), Video the video
/// flyout (MainPage.Video.cs); the frame entries are in MainPage.Frames.cs, and Stream opens the phone pairing card
/// (MainPage.LanStream.cs). UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // The Recent submenu is 12 wider than the menu: 288 less the stroke and the 4 inset either side.
    private const double RecentItemWidth = 278;

    private void InitializeContextMenu()
    {
        CtxOpenFile.Text = AppStrings.Get("Menu_OpenFile");
        CtxOpenUrl.Text = AppStrings.Get("Menu_OpenUrl");
        CtxRecent.Text = AppStrings.Get("Menu_Recent");
        CtxAudio.Text = AppStrings.Get("Menu_Audio");
        CtxSubtitles.Text = AppStrings.Get("Menu_Subtitles");
        CtxSpeed.Text = AppStrings.Get("Menu_Speed");
        CtxVideo.Text = AppStrings.Get("Menu_Video");
        CtxCopyFrame.Text = AppStrings.Get("Menu_CopyFrame");
        CtxSaveFrame.Text = AppStrings.Get("Menu_SaveFrame");
        CtxPictureInPicture.Text = AppStrings.Get("Menu_PictureInPicture");
        CtxAlwaysOnTop.Text = AppStrings.Get("Menu_AlwaysOnTop");
        CtxDiagnostics.Text = AppStrings.Get("Menu_Diagnostics");
        CtxSettings.Text = AppStrings.Get("Menu_Settings");
        CtxClips.Text = AppStrings.Get("Menu_Clips");
        CtxStream.Text = AppStrings.Get("Menu_Stream");

        foreach (double option in PlaybackSpeed.Options)
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = PlaybackSpeed.Format(option),
                Tag = option,
                Style = (Style)Application.Current.Resources["AetherToggleMenuItemStyle"],
            };
            item.Click += OnSpeedItemClick;
            CtxSpeed.Items.Add(item);
        }
        ShowSpeed(_session?.ViewModel.Speed ?? PlaybackSpeed.Normal);
    }

    private void ApplyContextMenuShortcuts()
    {
        CtxOpenFile.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.OpenFile);
        CtxOpenUrl.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.OpenUrl);
        CtxPictureInPicture.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.PictureInPicture);
        CtxAlwaysOnTop.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.AlwaysOnTop);
        CtxDiagnostics.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.Diagnostics);
        CtxSettings.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.Settings);
        CtxCopyFrame.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.CopyFrame);
        CtxSaveFrame.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.SaveFrame);
    }

    private void OnPageContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (_tour.IsActive)
        {
            args.Handled = true; // the tour opens the menu itself on its menu step
            return;
        }
        // Settings and the Open URL box keep their own right-click (copy and paste).
        if (_settingsShown || IsOpenUrlShown || args.OriginalSource is TextBox)
        {
            return;
        }
        if (_lanShown)
        {
            args.Handled = true; // the phone card is modal: no player menu over it
            return;
        }
        args.Handled = true;
        if (!args.TryGetPosition(VideoHost, out Point position))
        {
            position = new Point(VideoHost.ActualWidth / 2, VideoHost.ActualHeight / 2); // from the keyboard: no pointer
        }
        RefreshContextMenu();
        ContextMenu.ShowAt(VideoHost, new FlyoutShowOptions { Position = position, ShowMode = FlyoutShowMode.Standard });
    }

    private void RefreshContextMenu()
    {
        IReadOnlyList<RecentItem> recents = App.Recents.Items;
        var state = new ContextMenuState(
            EngineReady: _session is not null,
            HasVideo: HasVideo,
            RecentCount: recents.Count,
            ClipCount: App.Clips.Items.Count,
            HasWindow: _host is not null);
        CtxOpenFile.IsEnabled = state.OpenFile;
        CtxOpenUrl.IsEnabled = state.OpenUrl;
        CtxRecent.IsEnabled = state.Recent;
        CtxAudio.IsEnabled = state.Audio;
        CtxSubtitles.IsEnabled = state.Subtitles;
        CtxSpeed.IsEnabled = state.Speed;
        CtxVideo.IsEnabled = state.Video;
        CtxCopyFrame.IsEnabled = state.CopyFrame;
        CtxSaveFrame.IsEnabled = state.SaveFrame;
        CtxPictureInPicture.IsEnabled = state.PictureInPicture;
        CtxPictureInPicture.Text = AppStrings.Get(IsPictureInPicture ? "Pip_BackToFullWindow" : "Menu_PictureInPicture");
        CtxAlwaysOnTop.IsEnabled = state.AlwaysOnTop;
        CtxDiagnostics.IsEnabled = state.Diagnostics;
        CtxSettings.IsEnabled = state.Settings;
        CtxClips.IsEnabled = state.Clips;
        CtxStream.IsEnabled = state.Stream;
        SyncAlwaysOnTop();
        BuildRecentMenu(recents);
    }

    private void BuildRecentMenu(IReadOnlyList<RecentItem> recents)
    {
        CtxRecent.Items.Clear();
        var itemStyle = (Style)Application.Current.Resources["AetherMenuItemStyle"];
        foreach (RecentItem recent in recents)
        {
            var item = new MenuFlyoutItem
            {
                Text = ContextMenuState.RecentLabel(recent.DisplayName, recent.Location),
                Tag = recent,
                Style = itemStyle,
                MinWidth = RecentItemWidth,
                MaxWidth = RecentItemWidth,
            };
            ToolTipService.SetToolTip(item, recent.Location);
            item.Click += OnRecentMenuItemClick;
            CtxRecent.Items.Add(item);
        }
        CtxRecent.Items.Add(new MenuFlyoutSeparator());
        var clear = new MenuFlyoutItem
        {
            Text = AppStrings.Get("Menu_ClearList"),
            Style = itemStyle,
            MinWidth = RecentItemWidth,
        };
        clear.Click += OnClearRecentsClick;
        CtxRecent.Items.Add(clear);
    }

    private void OnRecentMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: RecentItem recent })
        {
            OpenRecent(recent);
        }
    }

    private void OnContextMenuOpened(object? sender, object e) => HoldForMenu(true);

    private void OnContextMenuClosed(object? sender, object e)
    {
        HoldForMenu(false);
        if (_tour.IsActive)
        {
            TourNextButton.Focus(FocusState.Programmatic);
        }
        else if (_lanShown)
        {
            LanStreamSwitch.Focus(FocusState.Programmatic);
        }
        else if (!IsOpenUrlShown)
        {
            Focus(FocusState.Programmatic); // keep Space and the frame-step keys on the page
        }
        ShowTracksIfRequested();
        ShowVideoIfRequested();
    }

    private void OnDiagnosticsMenuClick(object sender, RoutedEventArgs e) => ToggleDiagnostics();

    private void OnSettingsMenuClick(object sender, RoutedEventArgs e) => SetSettingsShown(true);

    private void OnClipsMenuClick(object sender, RoutedEventArgs e)
    {
        LeavePictureInPicture();
        ClipDrawer.Open();
    }

    private void OnSettingsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (IsOpenUrlShown)
        {
            return;
        }
        args.Handled = true;
        SetSettingsShown(true);
    }
}
