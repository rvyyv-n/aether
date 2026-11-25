using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Aether_App;

/// <summary>
/// Switches dark theme surface and stroke brushes in place for OLED Dark mode, restoring their original Dark values
/// when returning to Auto, Light, or Dark. UI thread only.
/// </summary>
public static class OledTheme
{
    private static readonly Dictionary<SolidColorBrush, Color> Originals = [];

    public static void Apply(bool oled)
    {
        if (!oled && Originals.Count == 0)
        {
            return;
        }
        foreach (string themeKey in (string[])["Dark", "Default"])
        {
            foreach ((string key, uint argb) in ThemeModes.OledBrushOverrides)
            {
                if (!ThemeTokens.TryGetByThemeKey<Brush>(themeKey, key, out Brush? raw) || raw is not SolidColorBrush brush)
                {
                    continue;
                }
                if (!Originals.TryGetValue(brush, out Color original))
                {
                    Originals[brush] = original = brush.Color;
                }
                brush.Color = oled
                    ? Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb)
                    : original;
            }
        }
    }
}
