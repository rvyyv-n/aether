using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class VideoPage : Page
{
    public VideoPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);

        binder.Choose(QualityPresetRow, QualityPresetSegmented, SettingKeys.QualityPreset,
            [
                new("efficient", AppStrings.Get("Settings_QualityPreset_Efficient")),
                new("balanced", AppStrings.Get("Settings_QualityPreset_Balanced")),
                new("high", AppStrings.Get("Settings_QualityPreset_HighQuality")),
            ],
            "Settings_QualityPreset_Title");

        binder.Choose(HardwareDecodingRow, HardwareDecodingCombo, SettingKeys.HardwareDecoding,
            [.. HardwareDecodingOptions.All.Select(o => new Choice(o.Value, AppStrings.Get(o.ResourceKey)))],
            "Settings_HardwareDecoding_Title");
        binder.RestartNote(HardwareDecodingRestartNote, SettingKeys.HardwareDecoding);

        binder.Toggle(SmoothMotionRow, SmoothMotionToggle, SettingKeys.SmoothMotion, "Settings_SmoothMotion_Title", "Settings_SmoothMotion_Description");

        binder.Toggle(DeinterlaceRow, DeinterlaceToggle, SettingKeys.Deinterlace, "Settings_Deinterlace_Title", "Settings_Deinterlace_Description");
        binder.Choose(DeinterlaceModeRow, DeinterlaceModeCombo, SettingKeys.DeinterlaceMode,
            [
                new("auto", AppStrings.Get("Settings_DeinterlaceMode_Auto")),
                new("yadif", AppStrings.Get("Settings_DeinterlaceMode_Yadif")),
                new("bwdif", AppStrings.Get("Settings_DeinterlaceMode_Bwdif")),
            ],
            "Settings_DeinterlaceMode_Title");
        binder.Track(() => DeinterlaceModeRow.Visibility = App.Settings.Get(SettingKeys.Deinterlace) is true ? Visibility.Visible : Visibility.Collapsed);

        binder.Choose(AspectRatioRow, AspectRatioCombo, SettingKeys.AspectRatio,
            [
                new("auto", AppStrings.Get("Settings_AspectRatio_Auto")),
                new("16:9", "16:9"),
                new("4:3", "4:3"),
                new("21:9", "21:9"),
                new("1:1", "1:1"),
                new("2.35:1", "2.35:1"),
            ],
            "Settings_AspectRatio_Title", "Settings_AspectRatio_Description");

        SnapshotsExpander.Header = AppStrings.Get("Settings_Snapshots_Title");
        SnapshotsExpander.Description = AppStrings.Get("Settings_Snapshots_Description");
        binder.TextEntry(ScreenshotDirectoryRow, ScreenshotDirectoryBox, SettingKeys.ScreenshotDirectory, "Settings_ScreenshotDirectory_Title", "Settings_ScreenshotDirectory_Description");
        binder.Choose(ScreenshotFormatRow, ScreenshotFormatCombo, SettingKeys.ScreenshotFormat,
            [
                new("png", "PNG"),
                new("jpg", "JPEG"),
                new("webp", "WebP"),
            ],
            "Settings_ScreenshotFormat_Title");
        binder.TextEntry(ScreenshotTemplateRow, ScreenshotTemplateBox, SettingKeys.ScreenshotTemplate, "Settings_ScreenshotTemplate_Title", "Settings_ScreenshotTemplate_Description");

        ScalingExpander.Header = AppStrings.Get("Settings_ScalingAndFilters_Title");
        ScalingExpander.Description = AppStrings.Get("Settings_ScalingAndFilters_Description");
        binder.Choose(UpscalingRow, UpscalingCombo, SettingKeys.Upscaling,
            [
                new("bilinear", AppStrings.Get("Settings_Upscaling_Bilinear")),
                new("spline36", AppStrings.Get("Settings_Upscaling_Spline36")),
                new("lanczos", AppStrings.Get("Settings_Upscaling_Lanczos")),
                new("ewa_lanczos", AppStrings.Get("Settings_Upscaling_EwaLanczos")),
            ],
            "Settings_Upscaling_Title");
        binder.ChangedHint(UpscalingRow, UpscalingReset, SettingKeys.Upscaling);

        binder.Choose(DownscalingRow, DownscalingCombo, SettingKeys.Downscaling,
            [
                new("bilinear", AppStrings.Get("Settings_Downscaling_Bilinear")),
                new("hermite", AppStrings.Get("Settings_Downscaling_Hermite")),
                new("mitchell", AppStrings.Get("Settings_Downscaling_Mitchell")),
                new("lanczos", AppStrings.Get("Settings_Downscaling_Lanczos")),
            ],
            "Settings_Downscaling_Title");
        binder.ChangedHint(DownscalingRow, DownscalingReset, SettingKeys.Downscaling);

        binder.Toggle(DebandingRow, DebandingToggle, SettingKeys.Debanding, "Settings_Debanding_Title");

        binder.Choose(HdrToneMappingRow, HdrToneMappingCombo, SettingKeys.HdrToneMapping,
            [
                new("auto", AppStrings.Get("Settings_HdrToneMapping_Auto")),
                new("bt.2390", AppStrings.Get("Settings_HdrToneMapping_Bt2390")),
                new("reinhard", AppStrings.Get("Settings_HdrToneMapping_Reinhard")),
                new("hable", AppStrings.Get("Settings_HdrToneMapping_Hable")),
                new("clip", AppStrings.Get("Settings_HdrToneMapping_Clip")),
            ],
            "Settings_HdrToneMapping_Title");
        binder.ChangedHint(HdrToneMappingRow, HdrToneMappingReset, SettingKeys.HdrToneMapping);

        AdjustmentsExpander.Header = AppStrings.Get("Settings_Adjustments_Title");
        AdjustmentsExpander.Description = AppStrings.Get("Settings_Adjustments_Description");
        binder.Slider(BrightnessRow, BrightnessSlider, BrightnessValue, SettingKeys.Brightness, "Settings_Brightness_Title");
        binder.Slider(ContrastRow, ContrastSlider, ContrastValue, SettingKeys.Contrast, "Settings_Contrast_Title");
        binder.Slider(GammaRow, GammaSlider, GammaValue, SettingKeys.Gamma, "Settings_Gamma_Title");
        binder.Slider(SaturationRow, SaturationSlider, SaturationValue, SettingKeys.Saturation, "Settings_Saturation_Title");
        string[] adjustments = [SettingKeys.Brightness, SettingKeys.Contrast, SettingKeys.Gamma, SettingKeys.Saturation];
        AdjustmentsReset.Content = AppStrings.Get("Common_Reset");
        binder.ResetButton(AdjustmentsReset, $"{AppStrings.Get("Common_Reset")}: {AdjustmentsExpander.Header}", adjustments);

        // A setting that is not at its default stays in view rather than hiding in a closed expander.
        AdjustmentsExpander.IsExpanded = adjustments.Any(key => !App.Settings.IsDefault(key));
        SnapshotsExpander.IsExpanded = new[] { SettingKeys.ScreenshotDirectory, SettingKeys.ScreenshotFormat, SettingKeys.ScreenshotTemplate }
            .Any(key => !App.Settings.IsDefault(key));
        ScalingExpander.IsExpanded = new[] { SettingKeys.Upscaling, SettingKeys.Downscaling, SettingKeys.Debanding, SettingKeys.HdrToneMapping }
            .Any(key => !App.Settings.IsDefault(key));
    }
}
