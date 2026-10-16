using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class AudioPage : Page
{
    public AudioPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);

        // The engine's device list is not available without starting it, so the list holds the system default plus whatever device is saved.
        binder.Choose(AudioOutputDeviceRow, AudioOutputDeviceCombo, SettingKeys.AudioOutputDevice,
            [new("", AppStrings.Get("Settings_OutputDevice_SystemDefault"))],
            "Settings_OutputDevice_Title", allowCustom: true);
        binder.Toggle(ExclusiveModeRow, ExclusiveModeToggle, SettingKeys.ExclusiveMode, "Settings_ExclusiveMode_Title", "Settings_ExclusiveMode_Description");
        binder.Toggle(PassthroughRow, PassthroughToggle, SettingKeys.Passthrough, "Settings_Passthrough_Title", "Settings_Passthrough_Description");
        binder.RestartNote(PassthroughRestartNote, SettingKeys.Passthrough);
        binder.Toggle(VolumeNormalisationRow, VolumeNormalisationToggle, SettingKeys.VolumeNormalisation, "Settings_VolumeNormalisation_Title", "Settings_VolumeNormalisation_Description");
        binder.Toggle(PitchCorrectionRow, PitchCorrectionToggle, SettingKeys.PitchCorrection, "Settings_PitchCorrection_Title", "Settings_PitchCorrection_Description");
        binder.Toggle(StereoDownmixRow, StereoDownmixToggle, SettingKeys.StereoDownmix, "Settings_StereoDownmix_Title", "Settings_StereoDownmix_Description");
    }
}
