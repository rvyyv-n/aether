using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class PlaybackPage : Page
{
    private readonly Dictionary<TransportBarControl, (SettingsRow Row, Button Up, Button Down, ToggleSwitch Toggle)> _barRows = [];
    private bool _updatingBarRows;

    public PlaybackPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        binder.Toggle(PreciseSeekingRow, PreciseSeekingToggle, SettingKeys.PreciseSeeking, "Settings_PreciseSeeking_Title", "Settings_PreciseSeeking_Description");
        binder.Toggle(ResumePlaybackRow, ResumePlaybackToggle, SettingKeys.ResumePlayback, "Settings_Resume_Title", "Settings_Resume_Description");
        binder.Toggle(LoopByDefaultRow, LoopByDefaultToggle, SettingKeys.LoopByDefault, "Settings_LoopByDefault_Title");
        binder.Toggle(PlayNextInFolderRow, PlayNextInFolderToggle, SettingKeys.PlayNextInFolder, "Settings_PlayNextFile_Title", "Settings_PlayNextFile_Description");
        SettingsPageBinder.Describe(ChapterAutoSkipRow, "Settings_ChapterAutoSkip_Title", "Settings_ChapterAutoSkip_Description");
        binder.Toggle(null, ChapterAutoSkipToggle, SettingKeys.ChapterAutoSkip, "Settings_ChapterAutoSkip_Title", "Settings_ChapterAutoSkip_Description");
        binder.TextEntry(null, ChapterSkipNamesBox, SettingKeys.ChapterSkipNames, "Settings_ChapterSkipNames_Title", "Settings_ChapterSkipNames_Description");
        binder.Track(() => ChapterSkipNamesBox.Visibility = ChapterAutoSkipToggle.IsOn ? Visibility.Visible : Visibility.Collapsed);
        binder.Toggle(PreventSleepRow, PreventSleepToggle, SettingKeys.PreventSleep, "Settings_PreventSleep_Title", "Settings_PreventSleep_Description");

        BarControlsHeading.Text = AppStrings.Get("Settings_BarControls_Title");
        SettingsPageBinder.Describe(BarControlsHeaderRow, "Settings_BarControls_Title", "Settings_BarControls_Description");
        BarControlsResetButton.Content = AppStrings.Get("Settings_BarControls_Reset");
        AutomationProperties.SetName(BarControlsResetButton, AppStrings.Get("Settings_BarControls_Reset"));
        BarControlsResetButton.Click += (_, _) =>
        {
            if (App.Settings.Reset(SettingKeys.TransportBarControls))
            {
                SettingsSaver.Save();
            }
        };

        BuildBarRows();
        binder.Track(SyncBarRows);
    }

    private void BuildBarRows()
    {
        foreach (TransportBarItem item in TransportBarLayout.Parse(null))
        {
            TransportBarControl control = item.Control;
            string label = BarControlLabel(control);

            var up = new Button
            {
                Content = "\uE70E",
                Style = (Style)Application.Current.Resources["AetherIconButtonStyle"],
            };
            string upTip = AppStrings.Get("Settings_BarControl_MoveUp");
            ToolTipService.SetToolTip(up, upTip);
            AutomationProperties.SetName(up, $"{label} — {upTip}");
            up.Click += (_, _) => MoveBarControl(control, -1);

            var down = new Button
            {
                Content = "\uE70D",
                Style = (Style)Application.Current.Resources["AetherIconButtonStyle"],
            };
            string downTip = AppStrings.Get("Settings_BarControl_MoveDown");
            ToolTipService.SetToolTip(down, downTip);
            AutomationProperties.SetName(down, $"{label} — {downTip}");
            down.Click += (_, _) => MoveBarControl(control, +1);

            var toggle = new ToggleSwitch
            {
                Style = (Style)Application.Current.Resources["AetherSettingsToggleStyle"],
            };
            AutomationProperties.SetName(toggle, label);
            toggle.Toggled += (_, _) =>
            {
                if (!_updatingBarRows)
                {
                    SetBarControlVisible(control, toggle.IsOn);
                }
            };

            var controls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
            };
            controls.Children.Add(up);
            controls.Children.Add(down);
            controls.Children.Add(toggle);

            var row = new SettingsRow
            {
                Glyph = BarControlGlyph(control),
                Header = label,
                Content = controls,
            };
            _barRows[control] = (row, up, down, toggle);
        }
    }

    private void SyncBarRows()
    {
        _updatingBarRows = true;
        try
        {
            IReadOnlyList<TransportBarItem> items = TransportBarLayout.Parse(App.Settings.Current.TransportBarControls);
            BarControlsListGroup.Children.Clear();
            for (int i = 0; i < items.Count; i++)
            {
                TransportBarItem item = items[i];
                if (_barRows.TryGetValue(item.Control, out (SettingsRow Row, Button Up, Button Down, ToggleSwitch Toggle) entry))
                {
                    entry.Up.IsEnabled = i > 0;
                    entry.Down.IsEnabled = i < items.Count - 1;
                    entry.Toggle.IsOn = item.Visible;
                    BarControlsListGroup.Children.Add(entry.Row);
                }
            }
            BarControlsResetButton.IsEnabled = !App.Settings.IsDefault(SettingKeys.TransportBarControls);
        }
        finally
        {
            _updatingBarRows = false;
        }
    }

    private static void SetBarControlVisible(TransportBarControl control, bool visible)
    {
        string updated = TransportBarLayout.SetVisible(App.Settings.Current.TransportBarControls, control, visible);
        if (App.Settings.Set(SettingKeys.TransportBarControls, updated))
        {
            SettingsSaver.Save();
        }
    }

    private static void MoveBarControl(TransportBarControl control, int delta)
    {
        string updated = TransportBarLayout.Move(App.Settings.Current.TransportBarControls, control, delta);
        if (App.Settings.Set(SettingKeys.TransportBarControls, updated))
        {
            SettingsSaver.Save();
        }
    }

    private static string BarControlLabel(TransportBarControl control) => control switch
    {
        TransportBarControl.Speed => AppStrings.Get("Settings_BarControl_Speed"),
        TransportBarControl.Quality => AppStrings.Get("Settings_BarControl_Quality"),
        TransportBarControl.Skip => AppStrings.Get("Settings_BarControl_Skip"),
        TransportBarControl.FrameStep => AppStrings.Get("Settings_BarControl_FrameStep"),
        TransportBarControl.Tracks => AppStrings.Get("Settings_BarControl_Tracks"),
        TransportBarControl.Loop => AppStrings.Get("Settings_BarControl_Loop"),
        TransportBarControl.AlwaysOnTop => AppStrings.Get("Settings_BarControl_AlwaysOnTop"),
        TransportBarControl.Chapters => AppStrings.Get("Settings_BarControl_Chapters"),
        _ => throw new ArgumentOutOfRangeException(nameof(control), control, null),
    };

    private static string BarControlGlyph(TransportBarControl control) => control switch
    {
        TransportBarControl.Speed => "\uEC4A",
        TransportBarControl.Quality => "\uE714",
        TransportBarControl.Skip => "\uE72C",
        TransportBarControl.FrameStep => "\uE76C",
        TransportBarControl.Tracks => "\uE7F0",
        TransportBarControl.Loop => "\uE8EE",
        TransportBarControl.AlwaysOnTop => "\uE718",
        TransportBarControl.Chapters => "\uE893",
        _ => "\uE8FD",
    };
}
