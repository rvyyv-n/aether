using System.Runtime.InteropServices;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Updates;
using Aether_App.Updates;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Aether_App.Settings.Pages;

public sealed partial class AboutPage : Page
{
    private static readonly Uri Repository = new(UpdateFeed.RepositoryUrl);
    private static readonly Uri ReleaseNotes = new($"{UpdateFeed.RepositoryUrl}/releases");
    private static readonly Uri Licences = new($"{UpdateFeed.RepositoryUrl}/blob/master/THIRD_PARTY.md");
    private static readonly Uri Issues = new($"{UpdateFeed.RepositoryUrl}/issues");

    private readonly string _appVersion;
    private readonly string _decoder;
    private readonly string _renderer = AppStrings.Get("Settings_About_RendererValue");
    private string _mpvVersion = AppStrings.Get("Settings_About_Unknown");
    private string _ytDlpVersion = AppStrings.Get("Settings_About_Unknown");

    public AboutPage()
    {
        InitializeComponent();
        Version? version = typeof(App).Assembly.GetName().Version;
        _appVersion = version is null ? AppStrings.Get("Settings_About_Unknown") : $"{version.Major}.{version.Minor}.{version.Build}";

        IdentityRow.Header = $"{AppStrings.Get("App_Name")} {_appVersion}";
        IdentityRow.Description = AppStrings.Get("Settings_About_Credit");
        SetUpLink(ReleaseNotesButton, ReleaseNotesText, "Settings_About_ReleaseNotes", null, ReleaseNotes);
        SetUpLink(SourceCodeButton, SourceCodeText, "Settings_About_CreditSource", null, Repository);
        SetUpLink(LicencesButton, LicencesText, "Settings_About_Licences", "Settings_About_LicencesDescription", Licences);
        SetUpLink(ReportIssueButton, ReportIssueText, "Settings_About_ReportIssue", "Settings_About_ReportIssueDescription", Issues);
        UpdatesHeading.Text = AppStrings.Get("Settings_Updates_Title");
        HelpHeading.Text = AppStrings.Get("Settings_About_Help");

        // The engine is not asked what it settled on; the decoder shown is the one chosen in Video settings.
        string hwdec = App.Settings.Get(SettingKeys.HardwareDecoding) as string ?? "";
        _decoder = HardwareDecodingOptions.All.FirstOrDefault(o => o.Value == hwdec) is { } option
            ? AppStrings.Get(option.ResourceKey)
            : hwdec;

        EngineRow.Header = AppStrings.Get("Settings_About_Engine");
        ShowValue(DecoderLabel, DecoderText, "Settings_About_Decoder", _decoder);
        ShowValue(RendererLabel, RendererText, "Settings_About_Renderer", _renderer);
        ShowValue(MpvLabel, MpvVersionText, "Settings_About_MpvVersion", _mpvVersion);
        ShowValue(YtDlpLabel, YtDlpVersionText, "Settings_About_YtDlpVersion", _ytDlpVersion);

        CopyDiagnosticsText.Text = AppStrings.Get("Settings_About_CopyDiagnostics");
        AutomationProperties.SetName(CopyDiagnosticsButton, AppStrings.Get("Settings_About_CopyDiagnostics"));
        CopyDiagnosticsButton.Click += async (_, _) => await CopyDiagnosticsAsync();

        var binder = new SettingsPageBinder(this);
        binder.Toggle(UpdatesAutoRow, UpdatesAutoToggle, SettingKeys.UpdatesAutoCheck,
            "Settings_Updates_Auto_Title", "Settings_Updates_Auto_Description");
        UpdatesActionButton.Click += (_, _) =>
        {
            if (UpdateService.Controller.Status.State == UpdateState.Ready)
            {
                UpdateService.RestartToUpdate();
            }
            else
            {
                _ = UpdateService.CheckNowAsync();
            }
        };
        ShowUpdate(UpdateService.Controller.Status);
        Loaded += (_, _) =>
        {
            UpdateService.Controller.StatusChanged += OnUpdateStatusChanged;
            ShowUpdate(UpdateService.Controller.Status);
        };
        Unloaded += (_, _) => UpdateService.Controller.StatusChanged -= OnUpdateStatusChanged;

        TourTitle.Text = AppStrings.Get("Settings_About_Tour");
        TourDescription.Text = AppStrings.Get("Settings_About_TourDescription");
        AutomationProperties.SetName(TourButton, TourTitle.Text);
        AutomationProperties.SetHelpText(TourButton, TourDescription.Text);
        TourButton.Click += (_, _) => App.ShowTour();

        _ = LoadVersionsAsync();
    }

    private void OnUpdateStatusChanged(UpdateStatus status) => App.RunOnUi(() => ShowUpdate(status));

    private void ShowUpdate(UpdateStatus status)
    {
        // The status is the row's title, so the Updates heading above it is not repeated.
        UpdatesRow.Header = status.State switch
        {
            UpdateState.Checking => AppStrings.Get("Settings_Updates_Checking"),
            UpdateState.UpToDate => AppStrings.Format("Settings_Updates_UpToDate", _appVersion),
            UpdateState.Downloading => AppStrings.Format("Settings_Updates_Downloading", status.Version ?? "", (int)(status.Progress * 100)),
            UpdateState.Ready => AppStrings.Format("Settings_Updates_Ready", status.Version ?? ""),
            UpdateState.NotInstalled => AppStrings.Format("Settings_Updates_NotInstalled", _appVersion),
            UpdateState.Failed => AppStrings.Format("Settings_Updates_Failed", _appVersion),
            _ => AppStrings.Format("Settings_Updates_Version", _appVersion),
        };
        UpdatesActionButton.Content = AppStrings.Get(status.State == UpdateState.Ready ? "Settings_Updates_Restart" : "Settings_Updates_CheckNow");
        UpdatesActionButton.IsEnabled = status.CanCheck;
        UpdatesActionButton.Visibility = status.State == UpdateState.NotInstalled ? Visibility.Collapsed : Visibility.Visible;
        UpdatesAutoRow.IsEnabled = status.State != UpdateState.NotInstalled;
    }

    private static void SetUpLink(Button button, TextBlock text, string titleKey, string? helpKey, Uri target)
    {
        text.Text = AppStrings.Get(titleKey);
        AutomationProperties.SetName(button, text.Text);
        if (helpKey is not null)
        {
            string help = AppStrings.Get(helpKey);
            ToolTipService.SetToolTip(button, help);
            AutomationProperties.SetHelpText(button, help);
        }
        button.Click += async (_, _) => await Launcher.LaunchUriAsync(target);
    }

    private static void ShowValue(TextBlock label, TextBlock value, string labelKey, string text)
    {
        label.Text = AppStrings.Get(labelKey);
        value.Text = text;
        AutomationProperties.SetName(value, $"{label.Text}: {text}");
    }

    private async Task LoadVersionsAsync()
    {
        string? mpv = await Task.Run(ExternalTools.MpvVersion);
        if (mpv is not null)
        {
            _mpvVersion = mpv;
            ShowValue(MpvLabel, MpvVersionText, "Settings_About_MpvVersion", mpv);
        }
        string? ytDlp = await Task.Run(async () => ExternalTools.FindYtDlp() is { } path ? await ExternalTools.YtDlpVersionAsync(path) : null);
        if (ytDlp is not null)
        {
            _ytDlpVersion = ytDlp;
            ShowValue(YtDlpLabel, YtDlpVersionText, "Settings_About_YtDlpVersion", ytDlp);
        }
    }

    private async Task CopyDiagnosticsAsync()
    {
        string text = string.Join(Environment.NewLine,
            $"{AppStrings.Get("App_Name")} {_appVersion}",
            $"{AppStrings.Get("Settings_About_Decoder")}: {_decoder}",
            $"{AppStrings.Get("Settings_About_Renderer")}: {_renderer}",
            $"{AppStrings.Get("Settings_About_MpvVersion")}: {_mpvVersion}",
            $"{AppStrings.Get("Settings_About_YtDlpVersion")}: {_ytDlpVersion}",
            $"{AppStrings.Get("Settings_About_Windows")}: {RuntimeInformation.OSDescription}",
            $"{AppStrings.Get("Settings_About_Architecture")}: {RuntimeInformation.OSArchitecture}");
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);

        string label = AppStrings.Get("Settings_About_CopyDiagnostics");
        CopyDiagnosticsText.Text = AppStrings.Get("Settings_About_Copied");
        await Task.Delay(TimeSpan.FromSeconds(2));
        CopyDiagnosticsText.Text = label;
    }
}
