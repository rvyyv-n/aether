using System.Globalization;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Aether_App.Settings.Pages;

public sealed partial class SubtitlesPage : Page
{
    private static readonly string[] LanguageCodes =
        ["en", "es", "fr", "de", "it", "pt", "nl", "sv", "no", "da", "fi", "pl", "cs", "ru", "uk", "tr", "el", "ar", "he", "hi", "th", "vi", "id", "zh", "ja", "ko"];

    public SubtitlesPage()
    {
        InitializeComponent();
        DefaultsHeading.Text = AppStrings.Get("Settings_Group_Defaults");
        FontHeading.Text = AppStrings.Get("Settings_FontAndSize_Title");
        var binder = new SettingsPageBinder(this);

        binder.Toggle(ShowSubtitlesRow, ShowSubtitlesToggle, SettingKeys.ShowSubtitles, "Settings_ShowSubtitles_Title", "Settings_ShowSubtitles_Description");
        binder.Choose(SubtitleLanguageRow, SubtitleLanguageCombo, SettingKeys.PreferredSubtitleLanguage, Languages(),
            "Settings_PreferredSubtitleLanguage_Title", "Settings_PreferredSubtitleLanguage_Description", allowCustom: true);
        binder.Choose(AudioLanguageRow, AudioLanguageCombo, SettingKeys.PreferredAudioLanguage, Languages(),
            "Settings_PreferredAudioLanguage_Title", "Settings_PreferredAudioLanguage_Description", allowCustom: true);

        binder.Text(SubtitleFontRow, SubtitleFontBox, SettingKeys.SubtitleFont, "Settings_SubtitleFont_Title", "Settings_SubtitleFont_Description");
        binder.Choose(SubtitleSizeRow, SubtitleSizeSegmented, SettingKeys.SubtitleSize,
            [
                new("small", AppStrings.Get("Settings_SubtitleSize_Small")),
                new("medium", AppStrings.Get("Settings_SubtitleSize_Medium")),
                new("large", AppStrings.Get("Settings_SubtitleSize_Large")),
            ],
            "Settings_SubtitleSize_Title", "Settings_SubtitleSize_Description");
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
