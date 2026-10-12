using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Aether_App.Settings.Pages;

public sealed partial class AboutPage : Page
{
    private static readonly Uri Repository = new("https://github.com/rvyyv-n/aether");
    private static readonly Uri Issues = new("https://github.com/rvyyv-n/aether/issues");

    private readonly string _appVersion;
    private string _mpvVersion = AppStrings.Get("Settings_About_Unknown");
    private string _ytDlpVersion = AppStrings.Get("Settings_About_Unknown");

    public AboutPage()
    {
        InitializeComponent();
        Version? version = typeof(App).Assembly.GetName().Version;
        _appVersion = version is null ? AppStrings.Get("Settings_About_Unknown") : $"{version.Major}.{version.Minor}.{version.Build}";

        AppName.Text = AppStrings.Get("App_Name");
        AppBadge.Text = AppStrings.Get("App_Badge");
        AppVersion.Text = AppStrings.Format("Settings_About_Version", _appVersion);
        VersionsHeading.Text = AppStrings.Get("Settings_Group_Versions");
        SupportHeading.Text = AppStrings.Get("Settings_Group_Support");

        SettingsPageBinder.Describe(MpvRow, "Settings_About_MpvVersion", null);
        SettingsPageBinder.Describe(YtDlpRow, "Settings_About_YtDlpVersion", null);
        MpvVersionText.Text = _mpvVersion;
        YtDlpVersionText.Text = _ytDlpVersion;
        AutomationProperties.SetName(MpvVersionText, $"{MpvRow.Header}: {_mpvVersion}");
        AutomationProperties.SetName(YtDlpVersionText, $"{YtDlpRow.Header}: {_ytDlpVersion}");

        SettingsPageBinder.Describe(DiagnosticsRow, "Settings_About_CopyDiagnostics", null);
        CopyDiagnosticsButton.Content = AppStrings.Get("Settings_About_CopyDiagnostics");
        CopyDiagnosticsButton.Click += async (_, _) => await CopyDiagnosticsAsync();

        SettingsPageBinder.Describe(LicencesRow, "Settings_About_Licences", "Settings_About_LicencesDescription");
        LicencesButton.Content = AppStrings.Get("Settings_About_Licences");
        AutomationProperties.SetName(LicencesButton, AppStrings.Get("Settings_About_Licences"));
        LicencesButton.Click += async (_, _) => await Launcher.LaunchUriAsync(Repository);

        SettingsPageBinder.Describe(ReportIssueRow, "Settings_About_ReportIssue", null);
        ReportIssueButton.Content = AppStrings.Get("Settings_About_ReportIssue");
        ReportIssueButton.Click += async (_, _) => await Launcher.LaunchUriAsync(Issues);

        _ = LoadVersionsAsync();
    }

    private async Task LoadVersionsAsync()
    {
        string? mpv = await Task.Run(ExternalTools.MpvVersion);
        if (mpv is not null)
        {
            _mpvVersion = mpv;
            MpvVersionText.Text = mpv;
            AutomationProperties.SetName(MpvVersionText, $"{MpvRow.Header}: {mpv}");
        }
        string? ytDlp = await Task.Run(async () => ExternalTools.FindYtDlp() is { } path ? await ExternalTools.YtDlpVersionAsync(path) : null);
        if (ytDlp is not null)
        {
            _ytDlpVersion = ytDlp;
            YtDlpVersionText.Text = ytDlp;
            AutomationProperties.SetName(YtDlpVersionText, $"{YtDlpRow.Header}: {ytDlp}");
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        string text = string.Join(Environment.NewLine,
            $"{AppStrings.Get("App_Name")} {_appVersion}",
            $"{AppStrings.Get("Settings_About_MpvVersion")}: {_mpvVersion}",
            $"{AppStrings.Get("Settings_About_YtDlpVersion")}: {_ytDlpVersion}",
            $"Windows: {RuntimeInformation.OSDescription}",
            $"Architecture: {RuntimeInformation.OSArchitecture}");
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        string label = AppStrings.Get("Settings_About_CopyDiagnostics");
        CopyDiagnosticsButton.Content = AppStrings.Get("Settings_About_Copied");
        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyDiagnosticsButton.Content = label;
    }
}
