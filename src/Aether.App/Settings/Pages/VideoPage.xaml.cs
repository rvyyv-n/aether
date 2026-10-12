using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class VideoPage : Page
{
    public VideoPage()
    {
        InitializeComponent();
        QualityHeading.Text = AppStrings.Get("Settings_Group_Quality");
        ScalingHeading.Text = AppStrings.Get("Settings_ScalingAndFilters_Title");
        var binder = new SettingsPageBinder(this);

        binder.Choose(QualityPresetRow, QualityPresetSegmented, SettingKeys.QualityPreset,
            [
                new("efficient", AppStrings.Get("Settings_QualityPreset_Efficient")),
                new("balanced", AppStrings.Get("Settings_QualityPreset_Balanced")),
                new("high", AppStrings.Get("Settings_QualityPreset_HighQuality")),
            ],
            "Settings_QualityPreset_Title", "Settings_QualityPreset_Description");

        binder.Choose(HardwareDecodingRow, HardwareDecodingCombo, SettingKeys.HardwareDecoding,
            [.. HardwareDecodingOptions.All.Select(o => new Choice(o.Value, AppStrings.Get(o.ResourceKey)))],
            "Settings_HardwareDecoding_Title", "Settings_HardwareDecoding_Description");
        binder.RestartNote(HardwareDecodingRestartNote, SettingKeys.HardwareDecoding);

        binder.Toggle(SmoothMotionRow, SmoothMotionToggle, SettingKeys.SmoothMotion, "Settings_SmoothMotion_Title", "Settings_SmoothMotion_Description");

        binder.Choose(UpscalingRow, UpscalingCombo, SettingKeys.Upscaling,
            [
                new("bilinear", AppStrings.Get("Settings_Upscaling_Bilinear")),
                new("spline36", AppStrings.Get("Settings_Upscaling_Spline36")),
                new("lanczos", AppStrings.Get("Settings_Upscaling_Lanczos")),
                new("ewa_lanczos", AppStrings.Get("Settings_Upscaling_EwaLanczos")),
            ],
            "Settings_Upscaling_Title", "Settings_Upscaling_Description");

        binder.Choose(DownscalingRow, DownscalingCombo, SettingKeys.Downscaling,
            [
                new("bilinear", AppStrings.Get("Settings_Downscaling_Bilinear")),
                new("hermite", AppStrings.Get("Settings_Downscaling_Hermite")),
                new("mitchell", AppStrings.Get("Settings_Downscaling_Mitchell")),
                new("lanczos", AppStrings.Get("Settings_Downscaling_Lanczos")),
            ],
            "Settings_Downscaling_Title", "Settings_Downscaling_Description");

        binder.Toggle(DebandingRow, DebandingToggle, SettingKeys.Debanding, "Settings_Debanding_Title", "Settings_Debanding_Description");

        binder.Choose(HdrToneMappingRow, HdrToneMappingCombo, SettingKeys.HdrToneMapping,
            [
                new("auto", AppStrings.Get("Settings_HdrToneMapping_Auto")),
                new("bt.2390", AppStrings.Get("Settings_HdrToneMapping_Bt2390")),
                new("reinhard", AppStrings.Get("Settings_HdrToneMapping_Reinhard")),
                new("hable", AppStrings.Get("Settings_HdrToneMapping_Hable")),
                new("clip", AppStrings.Get("Settings_HdrToneMapping_Clip")),
            ],
            "Settings_HdrToneMapping_Title", "Settings_HdrToneMapping_Description");

        AdjustmentsExpander.Header = AppStrings.Get("Settings_Adjustments_Title");
        AdjustmentsExpander.Description = AppStrings.Get("Settings_Adjustments_Description");
        binder.Slider(BrightnessRow, BrightnessSlider, BrightnessValue, BrightnessReset, SettingKeys.Brightness, "Settings_Brightness_Title", "Settings_Brightness_Description");
        binder.Slider(ContrastRow, ContrastSlider, ContrastValue, ContrastReset, SettingKeys.Contrast, "Settings_Contrast_Title", "Settings_Contrast_Description");
        binder.Slider(GammaRow, GammaSlider, GammaValue, GammaReset, SettingKeys.Gamma, "Settings_Gamma_Title", "Settings_Gamma_Description");
        binder.Slider(SaturationRow, SaturationSlider, SaturationValue, SaturationReset, SettingKeys.Saturation, "Settings_Saturation_Title", "Settings_Saturation_Description");

        // An adjustment that is not at its default stays in view rather than hiding in a closed expander.
        AdjustmentsExpander.IsExpanded = new[] { SettingKeys.Brightness, SettingKeys.Contrast, SettingKeys.Gamma, SettingKeys.Saturation }
            .Any(key => !App.Settings.IsDefault(key));
    }
}
