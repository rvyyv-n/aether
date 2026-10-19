using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class StreamingPage : Page
{
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
        binder.Slider(NetworkCacheRow, NetworkCacheSlider, NetworkCacheValue, SettingKeys.NetworkCacheMegabytes,
            "Settings_NetworkCache_Title", "Settings_NetworkCache_Description",
            megabytes => AppStrings.Format("Settings_NetworkCache_Value", megabytes));
        binder.RestartNote(NetworkCacheRestartNote, SettingKeys.NetworkCacheMegabytes);
        HttpProxyBox.PlaceholderText = AppStrings.Get("Settings_HttpProxy_Placeholder");
        binder.TextEntry(HttpProxyRow, HttpProxyBox, SettingKeys.HttpProxy, "Settings_HttpProxy_Title", "Settings_HttpProxy_Description");
        binder.Choose(RtspTransportRow, RtspTransportCombo, SettingKeys.RtspTransport,
            [
                new("tcp", "TCP"),
                new("udp", "UDP"),
                new("http", "HTTP"),
                new("udp_multicast", AppStrings.Get("Settings_RtspTransport_Multicast")),
            ],
            "Settings_RtspTransport_Title", "Settings_RtspTransport_Description");

        YtDlpRow.Header = AppStrings.Get("Settings_YtDlp_Title");
        YtDlpActionButton.Click += async (_, _) => await OnYtDlpActionAsync();
        YtDlpMissingNote.Text = AppStrings.Get("Settings_Streaming_YtDlpMissing");
        _ = LoadYtDlpAsync();
    }

    private async Task LoadYtDlpAsync()
    {
        _ytDlpPath = await ExternalTools.RefreshYtDlpAsync();
        if (_ytDlpPath is null)
        {
            ShowYtDlp(missing: true, AppStrings.Get("Settings_YtDlp_MissingMessage"));
            return;
        }
        string? version = ExternalTools.CachedVersion(_ytDlpPath);
        ShowYtDlp(missing: false, version is null
            ? AppStrings.Format("Settings_Streaming_InstalledAt", _ytDlpPath)
            : AppStrings.Format("Settings_About_Version", version));
        // The button only shows when there is something to update to; an unanswered check leaves it as a way to retry.
        YtDlpActionButton.Visibility = Visibility.Collapsed;
        bool? newer = await YtDlpManager.IsNewerAvailableAsync(version);
        if (newer != false && _ytDlpPath is not null)
        {
            YtDlpActionButton.Visibility = Visibility.Visible;
        }
    }

    private async Task OnYtDlpActionAsync()
    {
        bool installing = _ytDlpPath is null;
        YtDlpActionButton.IsEnabled = false;
        YtDlpActionButton.Content = AppStrings.Get("Settings_YtDlp_Updating");
        var progress = new Progress<double>(fraction =>
            YtDlpActionButton.Content = AppStrings.Format("YtDlp_InstallProgress", (int)(fraction * 100)));
        (YtDlpInstallOutcome outcome, string? version) = await YtDlpManager.InstallLatestAsync(onlyIfNewer: !installing, progress);
        YtDlpActionButton.IsEnabled = true;
        await LoadYtDlpAsync();
        switch (outcome)
        {
            case YtDlpInstallOutcome.Installed:
                AppToasts.Show("ytdlp-update", AppStrings.Format(installing ? "Toast_YtDlpInstalled" : "Toast_YtDlpUpdated", version ?? ""), ToastTone.Success);
                break;
            case YtDlpInstallOutcome.AlreadyCurrent:
                AppToasts.Show("ytdlp-update", AppStrings.Get("Toast_YtDlpUpToDate"), ToastTone.Success);
                break;
            default:
                YtDlpRow.Description = AppStrings.Get(YtDlpManager.FailureKey(outcome));
                YtDlpActionButton.Content = AppStrings.Get("Stream_Retry");
                YtDlpActionButton.Visibility = Visibility.Visible;
                AppToasts.Show("ytdlp-update", AppStrings.Get(installing ? "Toast_YtDlpInstallFailed" : "Toast_YtDlpUpdateFailed"), ToastTone.Failure);
                break;
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
