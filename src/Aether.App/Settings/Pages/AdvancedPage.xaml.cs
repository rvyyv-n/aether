using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Aether_App.Settings.Pages;

public sealed partial class AdvancedPage : Page
{
    private readonly SettingsPageBinder _binder;

    public AdvancedPage()
    {
        InitializeComponent();
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
        ResetAllButton.Content = AppStrings.Get("Settings_ResetAllSettings");
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
            CustomOptionsErrors.Children.Add(ErrorLine(AppStrings.Format(key, error.Line, error.Key ?? "")));
        }
        if (errors.Count == 0 && App.Settings.Set(SettingKeys.CustomMpvOptions, text))
        {
            SettingsSaver.Save();
        }
    }

    private static StackPanel ErrorLine(string message)
    {
        var resources = Application.Current.Resources;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line.Children.Add(new Ellipse
        {
            Style = (Style)resources["AetherStatusDotStyle"],
            Margin = new Thickness(0, 5, 0, 0),
            Fill = (Brush)resources["AetherStatusWarningBrush"],
        });
        line.Children.Add(new TextBlock
        {
            Text = message,
            Style = (Style)resources["AetherCaptionTextStyle"],
            TextWrapping = TextWrapping.Wrap,
        });
        return line;
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
            Background = (Brush)resources["AetherSurfaceDialogFallbackBrush"],
            BorderBrush = (Brush)resources["AetherStrokeSurfaceBrush"],
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
