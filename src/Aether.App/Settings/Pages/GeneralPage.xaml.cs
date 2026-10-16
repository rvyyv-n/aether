using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        binder.Toggle(BatterySaverRow, BatterySaverToggle, SettingKeys.BatterySaver, "Settings_BatterySaver_Title", "Settings_BatterySaver_Description");
        binder.Toggle(OpenInRunningWindowRow, OpenInRunningWindowToggle, SettingKeys.OpenInRunningWindow, "Settings_SingleInstance_Title", "Settings_SingleInstance_Description");

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

    // The recents list changes on whichever thread cleared or added to it.
    private void OnRecentsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshHistory);

    // The buttons are off while there is nothing to clear.
    private void RefreshHistory()
    {
        ClearRecentsButton.IsEnabled = App.Recents.Items.Count > 0;
        ClearResumeButton.IsEnabled = App.Resume.Entries.Count > 0;
    }
}
