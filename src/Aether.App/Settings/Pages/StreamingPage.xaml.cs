using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Aether_App.Settings.Pages;

public sealed partial class StreamingPage : Page
{
    private static readonly Uri YtDlpReleases = new("https://github.com/yt-dlp/yt-dlp/releases/latest");

    private string? _ytDlpPath;

    public StreamingPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);

        binder.Choose(StreamingQualityRow, StreamingQualityCombo, SettingKeys.StreamingQuality,
            [
                new("480p", AppStrings.Get("Settings_StreamingQuality_480p")),
                new("720p", AppStrings.Get("Settings_StreamingQuality_720p")),
                new("1080p", AppStrings.Get("Settings_StreamingQuality_1080p")),
                new("1440p", AppStrings.Get("Settings_StreamingQuality_1440p")),
                new("2160p", AppStrings.Get("Settings_StreamingQuality_2160p")),
                new("best", AppStrings.Get("Settings_StreamingQuality_Best")),
            ],
            "Settings_StreamingQuality_Title", "Settings_StreamingQuality_Description");
        binder.Toggle(PreferGpuDecodableFormatsRow, PreferGpuDecodableFormatsToggle, SettingKeys.PreferGpuDecodableFormats,
            "Settings_PreferHardwareDecode_Title", "Settings_PreferHardwareDecode_Description");
        binder.Toggle(YtDlpAutoUpdateRow, YtDlpAutoUpdateToggle, SettingKeys.YtDlpAutoUpdate,
            "Settings_YtDlp_UpdateAutomatically_Title", "Settings_YtDlp_UpdateAutomatically_Description");

        YtDlpRow.Header = AppStrings.Get("Settings_YtDlp_Title");
        YtDlpActionButton.Click += async (_, _) => await OnYtDlpActionAsync();
        YtDlpMissingNote.Text = AppStrings.Get("Settings_Streaming_YtDlpMissing");
        _ = LoadYtDlpAsync();
    }

    private async Task LoadYtDlpAsync()
    {
        _ytDlpPath = await Task.Run(ExternalTools.FindYtDlp);
        if (_ytDlpPath is null)
        {
            ShowYtDlp(missing: true, AppStrings.Get("Settings_YtDlp_MissingMessage"));
            return;
        }
        string? version = await ExternalTools.YtDlpVersionAsync(_ytDlpPath);
        ShowYtDlp(missing: false, version is null
            ? AppStrings.Format("Settings_Streaming_InstalledAt", _ytDlpPath)
            : AppStrings.Format("Settings_About_Version", version));
    }

    private async Task OnYtDlpActionAsync()
    {
        if (_ytDlpPath is null)
        {
            await Launcher.LaunchUriAsync(YtDlpReleases);
            return;
        }
        YtDlpActionButton.IsEnabled = false;
        YtDlpActionButton.Content = AppStrings.Get("Settings_YtDlp_Updating");
        bool updated = await ExternalTools.UpdateYtDlpAsync(_ytDlpPath);
        YtDlpActionButton.IsEnabled = true;
        await LoadYtDlpAsync();
        if (!updated)
        {
            YtDlpRow.Description = AppStrings.Get("Settings_YtDlp_UpdateFailed");
        }
    }

    // A missing yt-dlp switches the streaming settings off, since there is nothing for them to act on.
    private void ShowYtDlp(bool missing, string description)
    {
        YtDlpRow.Description = description;
        YtDlpActionButton.Content = AppStrings.Get(missing ? "Settings_YtDlp_Download" : "Settings_YtDlp_UpdateNow");
        YtDlpActionButton.Visibility = Visibility.Visible;
        YtDlpMissingNote.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        foreach (Control row in new Control[] { YtDlpAutoUpdateRow, StreamingQualityRow, PreferGpuDecodableFormatsRow })
        {
            row.IsEnabled = !missing;
        }
    }
}
