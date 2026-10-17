using System.Globalization;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Windows.UI.Core;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        BindFont(binder);
        binder.Choose(SubtitleFontRow, SubtitleSizeCombo, SettingKeys.SubtitleSize,
            [
                new("small", AppStrings.Get("Settings_SubtitleSize_Small")),
                new("medium", AppStrings.Get("Settings_SubtitleSize_Medium")),
                new("large", AppStrings.Get("Settings_SubtitleSize_Large")),
                new("xlarge", AppStrings.Get("Settings_SubtitleSize_ExtraLarge")),
            ],
            "Settings_FontAndSize_Title");
        binder.Choose(SubtitleLookRow, SubtitleColorCombo, SettingKeys.SubtitleColor,
            [
                new("white", AppStrings.Get("Settings_SubtitleColor_White")),
                new("yellow", AppStrings.Get("Settings_SubtitleColor_Yellow")),
                new("green", AppStrings.Get("Settings_SubtitleColor_Green")),
                new("cyan", AppStrings.Get("Settings_SubtitleColor_Cyan")),
            ],
            "Settings_ColorAndPosition_Title");
        binder.Choose(SubtitleLookRow, SubtitlePositionCombo, SettingKeys.SubtitlePosition,
            [
                new("bottom", AppStrings.Get("Settings_SubtitlePosition_Bottom")),
                new("raised", AppStrings.Get("Settings_SubtitlePosition_Raised")),
                new("top", AppStrings.Get("Settings_SubtitlePosition_Top")),
            ],
            "Settings_ColorAndPosition_Title");
        binder.Toggle(SubtitleBoldRow, SubtitleBoldToggle, SettingKeys.SubtitleBold, "Settings_SubtitleBold_Title");
        binder.Choose(SubtitleOutlineRow, SubtitleOutlineCombo, SettingKeys.SubtitleOutline,
            [
                new("none", AppStrings.Get("Settings_SubtitleOutline_None")),
                new("thin", AppStrings.Get("Settings_SubtitleOutline_Thin")),
                new("normal", AppStrings.Get("Settings_SubtitleOutline_Normal")),
                new("thick", AppStrings.Get("Settings_SubtitleOutline_Thick")),
            ],
            "Settings_Outline_Title");
        binder.Choose(SubtitleOutlineRow, SubtitleOutlineColorCombo, SettingKeys.SubtitleOutlineColor,
            [
                new("black", AppStrings.Get("Settings_SubtitleOutlineColor_Black")),
                new("gray", AppStrings.Get("Settings_SubtitleOutlineColor_Gray")),
                new("white", AppStrings.Get("Settings_SubtitleColor_White")),
            ],
            "Settings_Outline_Title");
        binder.Choose(SubtitleBackgroundRow, SubtitleBackgroundCombo, SettingKeys.SubtitleBackground,
            [
                new("none", AppStrings.Get("Settings_SubtitleBackground_None")),
                new("soft", AppStrings.Get("Settings_SubtitleBackground_Soft")),
                new("solid", AppStrings.Get("Settings_SubtitleBackground_Solid")),
            ],
            "Settings_SubtitleBackground_Title");
        binder.Toggle(SubtitleOverrideRow, SubtitleOverrideToggle, SettingKeys.SubtitleOverrideStyled,
            "Settings_SubtitleOverride_Title", "Settings_SubtitleOverride_Description");
        AutomationProperties.SetName(SubtitleOutlineCombo, AppStrings.Get("Settings_SubtitleOutlineSize_Title"));
        AutomationProperties.SetName(SubtitleOutlineColorCombo, AppStrings.Get("Settings_SubtitleOutlineColor_Title"));

        PreviewText.Text = AppStrings.Get("Settings_SubtitlePreview_Text");
        AutomationProperties.SetAccessibilityView(PreviewFrame, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        binder.Track(UpdatePreview);

        AutomationProperties.SetName(SubtitleColorCombo,AppStrings.Get("Settings_SubtitleColor_Title"));
        AutomationProperties.SetName(SubtitlePositionCombo, AppStrings.Get("Settings_SubtitlePosition_Title"));
        AutomationProperties.SetName(SubtitleFontBox, AppStrings.Get("Settings_SubtitleFont_Title"));
        AutomationProperties.SetName(SubtitleFontArrow, AppStrings.Get("Settings_SubtitleFont_Title"));
        AutomationProperties.SetName(SubtitleSizeCombo, AppStrings.Get("Settings_SubtitleSize_Title"));
    }

    private List<string> _fonts = [];

    // The list opens from the arrow or Alt+Down; typing filters it. A typed name is kept on Enter or when focus leaves,
    // and a blank box goes back to the default.
    private void BindFont(SettingsPageBinder binder)
    {
        SettingsPageBinder.Describe(SubtitleFontRow, "Settings_FontAndSize_Title", null);
        bool updating = false;

        void ShowAll()
        {
            SubtitleFontBox.ItemsSource = _fonts;
            SubtitleFontBox.IsSuggestionListOpen = true;
        }

        void Commit(string text)
        {
            string font = text.Trim();
            bool changed = font.Length == 0 ? App.Settings.Reset(SettingKeys.SubtitleFont) : App.Settings.Set(SettingKeys.SubtitleFont, font);
            if (changed)
            {
                SettingsSaver.Save();
            }
        }

        SubtitleFontBox.TextChanged += (box, e) =>
        {
            if (!updating && e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                box.ItemsSource = FontList.Filter(_fonts, box.Text);
            }
        };
        SubtitleFontBox.QuerySubmitted += (_, e) => Commit(e.ChosenSuggestion as string ?? e.QueryText);
        SubtitleFontBox.LostFocus += (_, _) => Commit(SubtitleFontBox.Text);
        SubtitleFontBox.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Down
                && InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
            {
                e.Handled = true;
                ShowAll();
            }
        };
        SubtitleFontArrow.Click += (_, _) =>
        {
            SubtitleFontBox.Focus(FocusState.Programmatic);
            ShowAll();
        };

        binder.Track(() =>
        {
            string stored = App.Settings.Get(SettingKeys.SubtitleFont) as string ?? "";
            if (!_fonts.Contains(stored, StringComparer.OrdinalIgnoreCase))
            {
                _fonts = FontList.Build(_fonts.Count == 0 ? SubtitleFonts : _fonts, stored);
            }
            // Never overwrite what is being typed.
            if (SubtitleFontBox.Text != stored && SubtitleFontBox.FocusState == FocusState.Unfocused)
            {
                updating = true;
                SubtitleFontBox.Text = stored;
                updating = false;
            }
        });

        // Reading every installed font takes a moment, so it happens off the UI thread.
        _ = LoadInstalledFontsAsync();
    }

    private async Task LoadInstalledFontsAsync()
    {
        List<string> installed = await Task.Run(InstalledFonts.Families);
        if (installed.Count > 0)
        {
            _fonts = FontList.Build(installed, App.Settings.Get(SettingKeys.SubtitleFont) as string);
        }
    }

    // The preview follows the same maps as mpv, so what it shows is what the video gets. The outline is not drawn.
    private void UpdatePreview()
    {
        AetherSettings s = App.Settings.Current;
        PreviewText.FontFamily = new FontFamily(s.SubtitleFont);
        PreviewText.FontWeight = s.SubtitleBold ? FontWeights.Bold : FontWeights.Normal;
        PreviewText.FontSize = double.Parse(MpvSettingsMap.SubtitleFontSize(s.SubtitleSize), CultureInfo.InvariantCulture) * 0.4;
        PreviewText.Foreground = new SolidColorBrush(ParseColour(MpvSettingsMap.SubtitleColor(s.SubtitleColor)));
        PreviewBox.Background = new SolidColorBrush(ParseColour(MpvSettingsMap.SubtitleBackColor(s.SubtitleBackground)));
    }

    // "#RRGGBB" or "#AARRGGBB".
    private static Windows.UI.Color ParseColour(string hex)
    {
        uint value = uint.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte alpha = hex.Length == 9 ? (byte)(value >> 24) : (byte)255;
        return Windows.UI.Color.FromArgb(alpha, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
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
