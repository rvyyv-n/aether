using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class AdvancedPage : Page
{
    private readonly SettingsPageBinder _binder;

    public AdvancedPage()
    {
        InitializeComponent();
        EngineHeading.Text = AppStrings.Get("Settings_Group_Engine");
        ResetHeading.Text = AppStrings.Get("Settings_Group_Reset");
        _binder = new SettingsPageBinder(this);

        SettingsPageBinder.Describe(CustomOptionsRow, "Settings_CustomOptions_Title", "Settings_CustomOptions_Description");
        AutomationProperties.SetName(CustomOptionsBox, CustomOptionsRow.Header);
        CustomOptionsBox.TextChanged += (_, _) => Validate();
        CustomOptionsBox.LostFocus += (_, _) => Validate();
        _binder.Track(() =>
        {
            // A box with mistakes in it holds text that was never saved; a change from elsewhere must not wipe it.
            string stored = App.Settings.Get(SettingKeys.CustomMpvOptions) as string ?? "";
            if (CustomOptionsErrors.Children.Count == 0 && CustomOptionsBox.Text != stored)
            {
                CustomOptionsBox.Text = stored;
            }
        });
        _binder.RestartNote(CustomOptionsRestartNote, SettingKeys.CustomMpvOptions);

        SettingsPageBinder.Describe(ResetAllRow, "Settings_ResetAllSettings_Title", "Settings_ResetAllSettings_Description");
        ResetAllButton.Content = AppStrings.Get("Common_Reset");
        AutomationProperties.SetName(ResetAllButton, AppStrings.Get("Settings_ResetAllSettings_Title"));
        ResetAllButton.Click += async (_, _) => await ConfirmResetAsync();
    }

    private void Validate()
    {
        string text = CustomOptionsBox.Text;
        IReadOnlyList<MpvOptionError> errors = MpvOptionsValidator.Validate(text);
        CustomOptionsErrors.Children.Clear();
        foreach (MpvOptionError error in errors)
        {
            string key = error.Kind switch
            {
                MpvOptionErrorKind.MissingEquals => "Settings_MpvOptions_Error_MissingEquals",
                MpvOptionErrorKind.MissingName => "Settings_MpvOptions_Error_MissingName",
                MpvOptionErrorKind.InvalidName => "Settings_MpvOptions_Error_InvalidName",
                MpvOptionErrorKind.Reserved => "Settings_MpvOptions_Error_Reserved",
                _ => "Settings_MpvOptions_Error_Duplicate",
            };
            CustomOptionsErrors.Children.Add(new TextBlock
            {
                Text = AppStrings.Format(key, error.Line, error.Key ?? ""),
                Style = (Style)Application.Current.Resources["AetherCaptionTextStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
        }
        if (errors.Count == 0 && App.Settings.Set(SettingKeys.CustomMpvOptions, text))
        {
            SettingsSaver.Save();
        }
    }

    private async Task ConfirmResetAsync()
    {
        var resources = Application.Current.Resources;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = AppStrings.Get("Settings_ResetAllSettings_Title"),
            Content = new TextBlock
            {
                Text = AppStrings.Get("Settings_ResetAllSettings_Description"),
                Style = (Style)resources["AetherBodyTextStyle"],
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = AppStrings.Get("Common_Reset"),
            CloseButtonText = AppStrings.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            PrimaryButtonStyle = (Style)resources["AetherEmphasisPillButtonStyle"],
            CloseButtonStyle = (Style)resources["AetherOutlinePillButtonStyle"],
            Background = (Microsoft.UI.Xaml.Media.Brush)resources["AetherSurfaceDialogFallbackBrush"],
            BorderBrush = (Microsoft.UI.Xaml.Media.Brush)resources["AetherStrokeSurfaceBrush"],
            CornerRadius = (CornerRadius)resources["AetherOverlayCornerRadius"],
            RequestedTheme = RequestedTheme,
        };
        dialog.Resources["ContentDialogMaxWidth"] = (double)resources["AetherSizeDialogWidth"];
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            App.Settings.ResetAll();
            SettingsSaver.Save();
            CustomOptionsErrors.Children.Clear();
        }
    }
}
