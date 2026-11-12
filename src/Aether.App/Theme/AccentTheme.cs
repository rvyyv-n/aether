using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Aether_App;

/// <summary>
/// Recolours the light and dark theme brushes for the accent setting (12.9). The brushes are changed in place, so
/// everything drawn with them follows at once; none puts back the colours they were built with. UI thread only.
/// </summary>
public static class AccentTheme
{
    private static readonly Dictionary<SolidColorBrush, Color> Originals = [];

    public static void Apply(string setting)
    {
        AccentColor? accent = AccentPalette.Resolve(setting);
        IReadOnlyDictionary<string, uint> overrides = AccentPalette.Overrides(accent ?? default);
        foreach (ElementTheme theme in (ElementTheme[])[ElementTheme.Light, ElementTheme.Dark])
        {
            foreach ((string key, uint argb) in overrides)
            {
                if (ThemeTokens.Get<Brush>(theme, key) is not SolidColorBrush brush)
                {
                    continue;
                }
                if (!Originals.TryGetValue(brush, out Color original))
                {
                    Originals[brush] = original = brush.Color;
                }
                brush.Color = accent is null
                    ? original
                    : Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            }
        }
    }
}
