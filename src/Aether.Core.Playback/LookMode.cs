namespace Aether.Core.Playback;

/// <summary>
/// Controls Aether's overall visual chrome style (12.2): Aether glass uses translucent rounded frosted surfaces
/// and Mica Alt; Native uses Windows Desktop Acrylic and standard 8 px corner radii while respecting the user's
/// chosen accent (staying monochrome when Accent is None).
/// </summary>
public static class LookModes
{
    public const string Aether = "aether";
    public const string Native = "native";

    public static bool IsNative(string? value) => string.Equals(value, Native, StringComparison.Ordinal);

    public static bool IsValid(string? value) => value is Aether or Native;
}

/// <summary>Corner radii (in DIPs) for surfaces under the active <see cref="LookModes"/> value.</summary>
public readonly record struct LookCornerRadii(
    double Transport,
    double PlayButton,
    double SidebarItem,
    double Tile,
    double SidePanel,
    double ClipCard,
    double Overlay,
    double TransportButton,
    double Control)
{
    public static LookCornerRadii Aether { get; } = new(24, 18, 18, 16, 16, 12, 8, 6, 4);

    public static LookCornerRadii Native { get; } = new(8, 8, 8, 8, 8, 8, 8, 8, 4);

    public static LookCornerRadii For(string? mode) => LookModes.IsNative(mode) ? Native : Aether;
}
