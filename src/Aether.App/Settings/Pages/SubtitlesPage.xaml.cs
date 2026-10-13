using System.Globalization;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class SubtitlesPage : Page
{
    private static readonly string[] LanguageCodes =
        ["en", "es", "fr", "de", "it", "pt", "nl", "sv", "no", "da", "fi", "pl", "cs", "ru", "uk", "tr", "el", "ar", "he", "hi", "th", "vi", "id", "zh", "ja", "ko"];

    private static readonly string[] SubtitleFonts =
        ["Segoe UI Variable", "Segoe UI", "Arial", "Calibri", "Verdana", "Tahoma", "Trebuchet MS", "Georgia", "Times New Roman", "Consolas"];

    public SubtitlesPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);

        binder.Toggle(ShowSubtitlesRow, ShowSubtitlesToggle, SettingKeys.ShowSubtitles, "Settings_ShowSubtitlesByDefault_Title");
        binder.Choose(AudioLanguageRow, AudioLanguageCombo, SettingKeys.PreferredAudioLanguage, Languages(),
            "Settings_PreferredAudioLang_Title", allowCustom: true);
        binder.Choose(SubtitleLanguageRow, SubtitleLanguageCombo, SettingKeys.PreferredSubtitleLanguage, Languages(),
            "Settings_PreferredSubtitleLang_Title", allowCustom: true);

        // Font and size share one row, so each box is named for what it sets rather than for the row.
        binder.Choose(SubtitleFontRow, SubtitleFontCombo, SettingKeys.SubtitleFont, [.. SubtitleFonts.Select(f => new Choice(f, f))],
            "Settings_FontAndSize_Title", allowCustom: true);
        binder.Choose(SubtitleFontRow, SubtitleSizeCombo, SettingKeys.SubtitleSize,
            [
                new("small", AppStrings.Get("Settings_SubtitleSize_Small")),
                new("medium", AppStrings.Get("Settings_SubtitleSize_Medium")),
                new("large", AppStrings.Get("Settings_SubtitleSize_Large")),
            ],
            "Settings_FontAndSize_Title");
        AutomationProperties.SetName(SubtitleFontCombo, AppStrings.Get("Settings_SubtitleFont_Title"));
        AutomationProperties.SetName(SubtitleSizeCombo, AppStrings.Get("Settings_SubtitleSize_Title"));

        // Any installed font can be typed in; a blank box goes back to the default.
        SubtitleFontCombo.TextSubmitted += (_, e) =>
        {
            e.Handled = true;
            string font = e.Text.Trim();
            bool changed = font.Length == 0 ? App.Settings.Reset(SettingKeys.SubtitleFont) : App.Settings.Set(SettingKeys.SubtitleFont, font);
            if (changed)
            {
                SettingsSaver.Save();
            }
        };
    }

    private static List<Choice> Languages()
    {
        List<Choice> choices = [new("", AppStrings.Get("Settings_Language_NoPreference"))];
        foreach (string code in LanguageCodes)
        {
            try
            {
                choices.Add(new Choice(code, CultureInfo.GetCultureInfo(code).DisplayName));
            }
            catch (CultureNotFoundException)
            {
                choices.Add(new Choice(code, code));
            }
        }
        return choices;
    }
}
