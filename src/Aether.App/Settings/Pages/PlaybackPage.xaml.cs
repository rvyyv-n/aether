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
        binder.Toggle(ResumePlaybackRow, ResumePlaybackToggle, SettingKeys.ResumePlayback, "Settings_ResumePlayback_Title", "Settings_ResumePlayback_Description");
        binder.Toggle(LoopByDefaultRow, LoopByDefaultToggle, SettingKeys.LoopByDefault, "Settings_LoopByDefault_Title", "Settings_LoopByDefault_Description");
        binder.Toggle(PlayNextInFolderRow, PlayNextInFolderToggle, SettingKeys.PlayNextInFolder, "Settings_PlayNextInFolder_Title", "Settings_PlayNextInFolder_Description");
        binder.Toggle(RememberWindowSizeRow, RememberWindowSizeToggle, SettingKeys.RememberWindowSize, "Settings_RememberWindowSize_Title", "Settings_RememberWindowSize_Description");
        binder.Toggle(AlwaysOnTopRow, AlwaysOnTopToggle, SettingKeys.AlwaysOnTop, "Settings_AlwaysOnTop_Title", "Settings_AlwaysOnTop_Description");
    }
}
