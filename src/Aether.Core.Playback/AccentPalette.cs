using System.Globalization;

namespace Aether.Core.Playback;

public readonly record struct AccentColor(byte R, byte G, byte B);

public readonly record struct AccentPreset(string Id, AccentColor Color);

/// <summary>
/// The accent colour choice (12.9): none, which keeps Aether black and white, one of the presets, or any hex colour,
/// and the theme brushes it recolours.
/// </summary>
public static class AccentPalette
{
    public const string None = "none";

    public static IReadOnlyList<AccentPreset> Presets { get; } =
    [
        new("cyan", new AccentColor(0x22, 0xD3, 0xEE)),
        new("ice", new AccentColor(0x7D, 0xB9, 0xF0)),
        new("mint", new AccentColor(0x4A, 0xDE, 0x9F)),
        new("amethyst", new AccentColor(0xA7, 0x8B, 0xFA)),
        new("sunset", new AccentColor(0xF5, 0x9E, 0x5B)),
        new("slate", new AccentColor(0x94, 0xA3, 0xB8)),
    ];

    private const uint DarkText = 0xFF0E1014;
    private const uint LightText = 0xFFFFFFFF;

    public static bool IsValid(string value) => value == None || Resolve(value) is not null;

    /// <summary>The colour a stored value stands for; null for none or anything unrecognised.</summary>
    public static AccentColor? Resolve(string value)
    {
        foreach (AccentPreset preset in Presets)
        {
            if (preset.Id == value)
            {
                return preset.Color;
            }
        }
        return value.StartsWith('#') && TryParseHex(value, out AccentColor color) ? color : null;
    }

    /// <summary>What to store for what the user typed or picked: <c>none</c>, a preset id or <c>#RRGGBB</c>; null when it is neither.</summary>
    public static string? Normalize(string text)
    {
        string trimmed = text.Trim().ToLowerInvariant();
        if (trimmed == None || Presets.Any(p => p.Id == trimmed))
        {
            return trimmed;
        }
        return TryParseHex(trimmed, out AccentColor c) ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
    }

    /// <summary>Reads <c>#RRGGBB</c> or <c>#RGB</c>, with or without the hash.</summary>
    public static bool TryParseHex(string text, out AccentColor color)
    {
        color = default;
        string hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
        }
        if (hex.Length != 6 || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rgb))
        {
            return false;
        }
        color = new AccentColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    /// <summary>Dark text and switch knobs read better on this accent than white ones (WCAG contrast).</summary>
    public static bool WantsDarkText(AccentColor color)
    {
        double luminance = 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        return (luminance + 0.05) / 0.05 > 1.05 / (luminance + 0.05);
    }

    private static double Linear(byte channel)
    {
        double c = channel / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// The theme brushes the accent recolours, as ARGB, the same in light and dark: switches and other accent fills
    /// take it solid, the emphasis fills (primary pill, play button, selected rail item and chip) a tint of it.
    /// </summary>
    public static IReadOnlyDictionary<string, uint> Overrides(AccentColor accent)
    {
        uint Argb(byte alpha) => (uint)alpha << 24 | (uint)accent.R << 16 | (uint)accent.G << 8 | accent.B;
        uint onAccent = WantsDarkText(accent) ? DarkText : LightText;
        return new Dictionary<string, uint>(StringComparer.Ordinal)
        {
            ["AccentFillColorDefaultBrush"] = Argb(0xFF),
            ["AccentFillColorSecondaryBrush"] = Argb(0xE6),
            ["AccentFillColorTertiaryBrush"] = Argb(0xCC),
            ["AccentTextFillColorPrimaryBrush"] = Argb(0xFF),
            ["TextOnAccentFillColorPrimaryBrush"] = onAccent,
            ["ToggleSwitchFillOn"] = Argb(0xFF),
            ["ToggleSwitchFillOnPointerOver"] = Argb(0xE6),
            ["ToggleSwitchFillOnPressed"] = Argb(0xCC),
            ["ToggleSwitchKnobFillOn"] = onAccent,
            ["ToggleSwitchKnobFillOnPointerOver"] = onAccent,
            ["ToggleSwitchKnobFillOnPressed"] = onAccent,
            ["AetherFillEmphasisRestBrush"] = Argb(0x47),
            ["AetherFillEmphasisHoverBrush"] = Argb(0x5C),
            ["AetherFillEmphasisPressedBrush"] = Argb(0x33),
        };
    }
}
