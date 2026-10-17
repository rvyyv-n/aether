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
        binder.Toggle(ChapterAutoSkipRow, ChapterAutoSkipToggle, SettingKeys.ChapterAutoSkip, "Settings_ChapterAutoSkip_Title", "Settings_ChapterAutoSkip_Description");
        binder.TextEntry(ChapterSkipNamesRow, ChapterSkipNamesBox, SettingKeys.ChapterSkipNames, "Settings_ChapterSkipNames_Title", "Settings_ChapterSkipNames_Description");
        binder.Track(() => ChapterSkipNamesBox.IsEnabled = ChapterAutoSkipToggle.IsOn);
        binder.Toggle(PreventSleepRow, PreventSleepToggle, SettingKeys.PreventSleep, "Settings_PreventSleep_Title", "Settings_PreventSleep_Description");
    }
}
