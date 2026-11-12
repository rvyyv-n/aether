using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Aether_App.Settings.Pages;

public sealed partial class GeneralPage : Page
{
    private bool _showingAccent;

    public GeneralPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        binder.Choose(ThemeRow, ThemeSegmented, SettingKeys.Theme,
            [new("auto", AppStrings.Get("Settings_Theme_Auto")),
             new("light", AppStrings.Get("Settings_Theme_Light")),
             new("dark", AppStrings.Get("Settings_Theme_Dark"))],
            "Settings_Theme_Title", "Settings_Theme_Description");
        BindAccent(binder);
        binder.Toggle(BatterySaverRow, BatterySaverToggle, SettingKeys.BatterySaver, "Settings_BatterySaver_Title", "Settings_BatterySaver_Description");
        binder.Toggle(OpenInRunningWindowRow, OpenInRunningWindowToggle, SettingKeys.OpenInRunningWindow, "Settings_SingleInstance_Title", "Settings_SingleInstance_Description");
        binder.Toggle(RememberWindowSizeRow, RememberWindowSizeToggle, SettingKeys.RememberWindowSize, "Settings_RememberWindowSize_Title", "Settings_RememberWindowSize_Description");
        binder.Toggle(AlwaysOnTopRow, AlwaysOnTopToggle, SettingKeys.AlwaysOnTop, "Settings_AlwaysOnTop_Title", "Settings_AlwaysOnTop_Description");

        SettingsPageBinder.Describe(ClearRecentsRow, "Settings_ClearRecents_Title", "Settings_ClearRecents_Description");
        SettingsPageBinder.Describe(ClearResumeRow, "Settings_ClearResume_Title", "Settings_ClearResume_Description");
        ClearRecentsButton.Content = AppStrings.Get("Settings_ClearRecents_Button");
        ClearResumeButton.Content = AppStrings.Get("Settings_ClearResume_Button");
        AutomationProperties.SetName(ClearRecentsButton, ClearRecentsRow.Header);
        AutomationProperties.SetName(ClearResumeButton, ClearResumeRow.Header);
        ClearRecentsButton.Click += (_, _) => _ = Task.Run(App.Recents.Clear);
        ClearResumeButton.Click += (_, _) =>
        {
            _ = Task.Run(App.Resume.Clear).ContinueWith(_ => DispatcherQueue.TryEnqueue(RefreshHistory), TaskScheduler.Default);
        };

        Loaded += (_, _) =>
        {
            App.Recents.Changed -= OnRecentsChanged;
            App.Recents.Changed += OnRecentsChanged;
            RefreshHistory();
        };
        Unloaded += (_, _) => App.Recents.Changed -= OnRecentsChanged;
    }

    // The swatches come from the XAML, one per preset id; their colours from AccentPalette.
    private void BindAccent(SettingsPageBinder binder)
    {
        SettingsPageBinder.Describe(AccentRow, "Settings_Accent_Title", "Settings_Accent_Description");
        foreach (ListBoxItem item in AccentSwatches.Items.Cast<ListBoxItem>())
        {
            string id = (string)item.Tag;
            if (AccentPalette.Resolve(id) is { } c)
            {
                ((Ellipse)item.Content).Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B));
            }
            string name = AccentName(id);
            AutomationProperties.SetName(item, name);
            ToolTipService.SetToolTip(item, name);
        }
        AutomationProperties.SetName(AccentSwatches, AccentRow.Header);
        AutomationProperties.SetName(AccentHex, AppStrings.Get("Settings_Accent_Custom"));
        ToolTipService.SetToolTip(AccentHex, AppStrings.Get("Settings_Accent_Custom"));

        AccentSwatches.SelectionChanged += (_, _) =>
        {
            if (!_showingAccent && AccentSwatches.SelectedItem is ListBoxItem { Tag: string id })
            {
                SaveAccent(id);
            }
        };
        AccentHex.LostFocus += (_, _) => CommitHex();
        AccentHex.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                CommitHex();
            }
        };
        binder.Track(() =>
        {
            _showingAccent = true;
            string value = App.Settings.Current.Accent;
            AccentSwatches.SelectedItem = AccentSwatches.Items.Cast<ListBoxItem>().FirstOrDefault(i => (string)i.Tag == value);
            AccentHex.Text = value.StartsWith('#') ? value : "";
            _showingAccent = false;
        });
    }

    private static string AccentName(string id) => id switch
    {
        "cyan" => AppStrings.Get("Settings_Accent_Cyan"),
        "ice" => AppStrings.Get("Settings_Accent_Ice"),
        "mint" => AppStrings.Get("Settings_Accent_Mint"),
        "amethyst" => AppStrings.Get("Settings_Accent_Amethyst"),
        "sunset" => AppStrings.Get("Settings_Accent_Sunset"),
        "slate" => AppStrings.Get("Settings_Accent_Slate"),
        _ => AppStrings.Get("Settings_Accent_None"),
    };

    // A typed colour applies once it is a whole hex colour; anything else is left for the user to finish.
    private void CommitHex()
    {
        if (AccentPalette.Normalize(AccentHex.Text) is { } value && value.StartsWith('#'))
        {
            SaveAccent(value);
        }
    }

    private static void SaveAccent(string value)
    {
        if (App.Settings.Set(SettingKeys.Accent, value))
        {
            SettingsSaver.Save();
        }
    }

    // The recents list changes on whichever thread cleared or added to it.
    private void OnRecentsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshHistory);

    // The buttons are off while there is nothing to clear.
    private void RefreshHistory()
    {
        ClearRecentsButton.IsEnabled = App.Recents.Items.Count > 0;
        ClearResumeButton.IsEnabled = App.Resume.Entries.Count > 0;
    }
}
