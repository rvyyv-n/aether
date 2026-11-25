namespace Aether.Core.Playback;

/// <summary>
/// Theme choices and the in-place dark brush overrides for OLED mode (pure black canvas with crisp elevated obsidian
/// cards and subtle 1 px borders).
/// </summary>
public static class ThemeModes
{
    public const string Auto = "auto";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Oled = "oled";

    public static bool IsOled(string? value) => string.Equals(value, Oled, StringComparison.Ordinal);

    /// <summary>
    /// Dark theme brushes overridden when <see cref="Oled"/> is active, as ARGB. None of these keys overlap with
    /// <see cref="AccentPalette.Overrides"/>, so OLED and custom accents compose cleanly.
    /// </summary>
    public static IReadOnlyDictionary<string, uint> OledBrushOverrides { get; } = new Dictionary<string, uint>(StringComparer.Ordinal)
    {
        ["AetherSurfaceLayerBrush"] = 0xFF000000u,
        ["AetherSurfaceCardBrush"] = 0xFF0B0D10u,
        ["AetherSurfaceGlassBrush"] = 0xEB0A0C0Fu,
        ["AetherSurfaceDialogBrush"] = 0xEB0A0C0Fu,
        ["AetherSurfaceDialogFallbackBrush"] = 0xFF0A0C0Fu,
        ["AetherSurfaceOverlayStrongBrush"] = 0xFF0A0C0Fu,
        ["AetherSurfaceMenuBrush"] = 0xEB0D0F12u,
        ["AetherSurfaceMenuFallbackBrush"] = 0xFF0D0F12u,
        ["AetherSurfaceBarBrush"] = 0xCC060709u,
        ["AetherSurfaceOsdBrush"] = 0xCC060709u,
        ["AetherStrokeSurfaceBrush"] = 0x21FFFFFFu,
        ["AetherStrokeControlBrush"] = 0x2BFFFFFFu,
        ["AetherStrokeDividerBrush"] = 0x1AFFFFFFu,
        ["AetherStrokeLayerBrush"] = 0x1FFFFFFFu,
        ["AetherStrokeBarBrush"] = 0x1FFFFFFFu,
        ["AetherFillInputBrush"] = 0x12FFFFFFu,
    };
}
