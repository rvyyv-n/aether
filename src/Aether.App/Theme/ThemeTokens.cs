using Microsoft.UI.Xaml;

namespace Aether_App;

/// <summary>
/// Looks up design tokens for a given theme. Application.Current.Resources always resolves against the Windows
/// theme, so code that picks colours must come through here to follow the in-app choice or a forced-dark surface.
/// </summary>
public static class ThemeTokens
{
    private static readonly Dictionary<(string Theme, string Key), object> Cache = [];
    private static readonly Windows.UI.ViewManagement.UISettings System = new();

    /// <summary>
    /// The theme the user's Appearance choice gives right now: Light or Dark as picked, or Windows' own when on Auto. Read
    /// from the setting rather than from an element's ActualTheme, which can still show the old theme just after the
    /// choice changes, and Application.RequestedTheme, which stays at what Windows was at launch.
    /// </summary>
    public static ElementTheme AppTheme => App.Settings.Current.Theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => IsLight(System.GetColorValue(Windows.UI.ViewManagement.UIColorType.Background)) ? ElementTheme.Light : ElementTheme.Dark,
    };

    private static bool IsLight(Windows.UI.Color background) => background.R + background.G + background.B > 382;

    public static T Get<T>(FrameworkElement element, string key) => Get<T>(element.ActualTheme, key);

    public static T Get<T>(ElementTheme theme, string key)
    {
        if (theme == ElementTheme.Default)
        {
            theme = Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        }
        string name = theme == ElementTheme.Light ? "Light" : "Dark";
        if (Cache.TryGetValue((name, key), out object? cached))
        {
            return (T)cached;
        }
        if (TryFind(Application.Current.Resources, name, key, out object? value))
        {
            Cache[(name, key)] = value!;
            return (T)value!;
        }
        return (T)Application.Current.Resources[key];
    }

    // Searches every merged dictionary, since the Windows App SDK's own controls resources have Light and Dark
    // dictionaries too and must not be mistaken for Aether's; a token missing from the one that was found would fall
    // back to Windows' theme and come out in the wrong colours.
    private static bool TryFind(ResourceDictionary dictionary, string theme, string key, out object? value)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out object? themed)
            && themed is ResourceDictionary themeDictionary
            && themeDictionary.TryGetValue(key, out value))
        {
            return true;
        }
        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (TryFind(merged, theme, key, out value))
            {
                return true;
            }
        }
        value = null;
        return false;
    }
}
