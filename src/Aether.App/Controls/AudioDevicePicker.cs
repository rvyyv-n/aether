using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;
using Aether_App.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Controls;

/// <summary>Shared binding logic for populating and applying audio output device combo boxes in Tracks and Settings.</summary>
internal static class AudioDevicePicker
{
    public static IReadOnlyList<AudioDeviceOption> Sync(
        ComboBox combo,
        AudioDeviceList devices,
        IReadOnlyList<AudioDeviceOption> currentOptions)
    {
        IReadOnlyList<AudioDeviceOption> options = devices.BuildOptions(
            App.Settings.Current.AudioOutputDevice,
            AppStrings.Get("Settings_OutputDevice_SystemDefault"),
            AppStrings.Get("Settings_OutputDevice_Unavailable"));

        if (!currentOptions.SequenceEqual(options))
        {
            combo.Items.Clear();
            foreach (AudioDeviceOption option in options)
            {
                combo.Items.Add(option.Label);
            }
        }
        else
        {
            options = currentOptions;
        }

        string current = AudioDeviceList.NormalizeSettingValue(App.Settings.Current.AudioOutputDevice);
        int selectedIndex = 0;
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Value, current, StringComparison.Ordinal))
            {
                selectedIndex = i;
                break;
            }
        }
        combo.SelectedIndex = selectedIndex;
        return options;
    }

    public static void ApplySelection(ComboBox combo, IReadOnlyList<AudioDeviceOption> options)
    {
        if (combo.SelectedIndex < 0 || combo.SelectedIndex >= options.Count)
        {
            return;
        }

        string value = options[combo.SelectedIndex].Value;
        if (App.Settings.Set(SettingKeys.AudioOutputDevice, value))
        {
            SettingsSaver.Save();
        }
    }
}
