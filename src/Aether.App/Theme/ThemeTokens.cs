using Microsoft.UI.Xaml;

namespace Aether_App;

/// <summary>
/// Looks up design tokens for a given theme. Application.Current.Resources always resolves against the Windows
/// theme, so code that picks colours must come through here to follow the in-app choice or a forced-dark surface.
/// </summary>
public static class ThemeTokens
{
    private static ResourceDictionary? _light;
    private static ResourceDictionary? _dark;

    public static T Get<T>(FrameworkElement element, string key) => Get<T>(element.ActualTheme, key);

    public static T Get<T>(ElementTheme theme, string key)
    {
        if (theme == ElementTheme.Default)
        {
            theme = Application.Current.RequestedTheme == ApplicationTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        }
        Find();
        ResourceDictionary? dictionary = theme == ElementTheme.Light ? _light : _dark;
        if (dictionary is not null && dictionary.TryGetValue(key, out object? value))
        {
            return (T)value;
        }
        return (T)Application.Current.Resources[key];
    }

    private static void Find()
    {
        if (_light is not null && _dark is not null)
        {
            return;
        }
        foreach (ResourceDictionary merged in Application.Current.Resources.MergedDictionaries)
        {
            if (merged.ThemeDictionaries.TryGetValue("Light", out object? light)
                && merged.ThemeDictionaries.TryGetValue("Dark", out object? dark))
            {
                _light = (ResourceDictionary)light;
                _dark = (ResourceDictionary)dark;
                return;
            }
        }
    }
}
