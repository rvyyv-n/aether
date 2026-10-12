using Aether_App.Settings.Pages;

namespace Aether_App.Settings;

/// <summary>One entry in the settings section list: its icon, its name and the page it opens.</summary>
public sealed class SettingsSection(string glyph, string titleKey, Type pageType)
{
    public string Glyph { get; } = glyph;

    public string Title { get; } = AppStrings.Get(titleKey);

    public Type PageType { get; } = pageType;

    public static IReadOnlyList<SettingsSection> All { get; } =
    [
        new("", "Settings_Page_General", typeof(GeneralPage)),
        new("", "Settings_Page_Playback", typeof(PlaybackPage)),
        new("", "Settings_Page_Video", typeof(VideoPage)),
        new("", "Settings_Page_Audio", typeof(AudioPage)),
        new("", "Settings_Page_Subtitles", typeof(SubtitlesPage)),
        new("", "Settings_Page_Streaming", typeof(StreamingPage)),
        new("", "Settings_Page_Shortcuts", typeof(ShortcutsPage)),
        new("", "Settings_Page_Advanced", typeof(AdvancedPage)),
        new("", "Settings_Page_About", typeof(AboutPage)),
    ];
}
