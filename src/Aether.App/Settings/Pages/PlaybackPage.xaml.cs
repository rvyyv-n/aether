using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class PlaybackPage : Page
{
    public PlaybackPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        binder.Toggle(PreciseSeekingRow, PreciseSeekingToggle, SettingKeys.PreciseSeeking, "Settings_PreciseSeeking_Title", "Settings_PreciseSeeking_Description");
        binder.Toggle(ResumePlaybackRow, ResumePlaybackToggle, SettingKeys.ResumePlayback, "Settings_Resume_Title", "Settings_Resume_Description");
        binder.Toggle(LoopByDefaultRow, LoopByDefaultToggle, SettingKeys.LoopByDefault, "Settings_LoopByDefault_Title");
        binder.Toggle(PlayNextInFolderRow, PlayNextInFolderToggle, SettingKeys.PlayNextInFolder, "Settings_PlayNextFile_Title", "Settings_PlayNextFile_Description");
        binder.Toggle(RememberWindowSizeRow, RememberWindowSizeToggle, SettingKeys.RememberWindowSize, "Settings_RememberWindowSize_Title");
        binder.Toggle(PreventSleepRow, PreventSleepToggle, SettingKeys.PreventSleep, "Settings_PreventSleep_Title", "Settings_PreventSleep_Description");
        binder.Toggle(AlwaysOnTopRow, AlwaysOnTopToggle, SettingKeys.AlwaysOnTop, "Settings_AlwaysOnTop_Title", "Settings_AlwaysOnTop_Description");
    }
}
