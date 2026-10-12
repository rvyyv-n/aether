using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Aether_App.Settings.Pages;

public sealed partial class StreamingPage : Page
{
    private static readonly Uri YtDlpReleases = new("https://github.com/yt-dlp/yt-dlp/releases/latest");

    public StreamingPage()
    {
        InitializeComponent();
        YtDlpHeading.Text = AppStrings.Get("Settings_YtDlp_Title");
        StreamsHeading.Text = AppStrings.Get("Settings_Group_Streams");
        var binder = new SettingsPageBinder(this);

        binder.Toggle(YtDlpAutoUpdateRow, YtDlpAutoUpdateToggle, SettingKeys.YtDlpAutoUpdate, "Settings_YtDlpAutoUpdate_Title", "Settings_YtDlpAutoUpdate_Description");
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
            "Settings_PreferGpuDecodableFormats_Title", "Settings_PreferGpuDecodableFormats_Description");

        YtDlpRow.Header = AppStrings.Get("Settings_YtDlp_Title");
        YtDlpDownloadButton.Content = AppStrings.Get("Settings_YtDlp_Download");
        YtDlpDownloadButton.Click += async (_, _) => await Launcher.LaunchUriAsync(YtDlpReleases);
        YtDlpMissingNote.Text = AppStrings.Get("Settings_Streaming_YtDlpMissing");
        _ = LoadYtDlpAsync();
    }

    private async Task LoadYtDlpAsync()
    {
        string? path = await Task.Run(ExternalTools.FindYtDlp);
        if (path is null)
        {
            ShowYtDlp(missing: true, AppStrings.Get("Settings_YtDlp_MissingMessage"));
            return;
        }
        string? version = await ExternalTools.YtDlpVersionAsync(path);
        ShowYtDlp(missing: false, version ?? AppStrings.Format("Settings_Streaming_InstalledAt", path));
    }

    // A missing yt-dlp switches the streaming settings off, since there is nothing for them to act on.
    private void ShowYtDlp(bool missing, string description)
    {
        YtDlpRow.Description = description;
        YtDlpDownloadButton.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        YtDlpMissingNote.Visibility = missing ? Visibility.Visible : Visibility.Collapsed;
        foreach (Control row in new Control[] { YtDlpAutoUpdateRow, StreamingQualityRow, PreferGpuDecodableFormatsRow })
        {
            row.IsEnabled = !missing;
        }
    }
}
