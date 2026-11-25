using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;
using Aether_App.Controls;
using Aether_App.Playback;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class AudioPage : Page
{
    private AudioDeviceList _devices = AudioDeviceList.Empty;
    private IReadOnlyList<AudioDeviceOption> _deviceOptions = [];
    private bool _updatingOutputDevice;
    private int _deviceRefreshVersion;

    public AudioPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);

        SettingsPageBinder.Describe(AudioOutputDeviceRow, "Settings_OutputDevice_Title", "Settings_OutputDevice_Description");
        AutomationProperties.SetName(AudioOutputDeviceCombo, AudioOutputDeviceRow.Header);
        AudioOutputDeviceCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingOutputDevice)
            {
                AudioDevicePicker.ApplySelection(AudioOutputDeviceCombo, _deviceOptions);
            }
        };
        AudioOutputDeviceCombo.DropDownOpened += (_, _) => _ = RefreshOutputDevicesAsync();
        binder.Track(SyncOutputDevices);
        Loaded += (_, _) => _ = RefreshOutputDevicesAsync();

        binder.Toggle(ExclusiveModeRow, ExclusiveModeToggle, SettingKeys.ExclusiveMode, "Settings_ExclusiveMode_Title", "Settings_ExclusiveMode_Description");
        binder.Toggle(PassthroughRow, PassthroughToggle, SettingKeys.Passthrough, "Settings_Passthrough_Title", "Settings_Passthrough_Description");
        binder.RestartNote(PassthroughRestartNote, SettingKeys.Passthrough);
        binder.Toggle(VolumeNormalisationRow, VolumeNormalisationToggle, SettingKeys.VolumeNormalisation, "Settings_VolumeNormalisation_Title", "Settings_VolumeNormalisation_Description");
        binder.Toggle(PitchCorrectionRow, PitchCorrectionToggle, SettingKeys.PitchCorrection, "Settings_PitchCorrection_Title", "Settings_PitchCorrection_Description");
        binder.Toggle(StereoDownmixRow, StereoDownmixToggle, SettingKeys.StereoDownmix, "Settings_StereoDownmix_Title", "Settings_StereoDownmix_Description");
        binder.Toggle(NightModeRow, NightModeToggle, SettingKeys.NightMode, "Settings_NightMode_Title", "Settings_NightMode_Description");
        EqualizerExpander.Header = AppStrings.Get("Equalizer_Title");
        EqualizerExpander.Description = AppStrings.Get("Equalizer_Description");
        binder.Toggle(null, EqualizerToggle, SettingKeys.EqualizerEnabled, "Equalizer_Title");
        EqualizerExpander.IsExpanded = App.Settings.Current.EqualizerEnabled;
    }

    private void SyncOutputDevices()
    {
        _updatingOutputDevice = true;
        try
        {
            _deviceOptions = AudioDevicePicker.Sync(AudioOutputDeviceCombo, _devices, _deviceOptions);
        }
        finally
        {
            _updatingOutputDevice = false;
        }
    }

    private async Task RefreshOutputDevicesAsync()
    {
        if (App.Session is not { } session)
        {
            return;
        }

        int version = ++_deviceRefreshVersion;
        AudioDeviceList devices = await Task.Run(() => AudioDeviceList.Read(session.Context));
        if (version == _deviceRefreshVersion && IsLoaded && App.Session == session)
        {
            _devices = devices;
            SyncOutputDevices();
        }
    }
}
