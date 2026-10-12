using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class GeneralPage : Page
{
    public GeneralPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        binder.Toggle(BatterySaverRow, BatterySaverToggle, SettingKeys.BatterySaver, "Settings_BatterySaver_Title", "Settings_BatterySaver_Description");
        binder.Toggle(OpenInRunningWindowRow, OpenInRunningWindowToggle, SettingKeys.OpenInRunningWindow, "Settings_OpenInRunningWindow_Title", "Settings_OpenInRunningWindow_Description");
    }
}
