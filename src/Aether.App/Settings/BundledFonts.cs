namespace Aether_App.Settings;

/// <summary>
/// Subtitle fonts that ship with Aether (all SIL Open Font License), so a few clean modern choices are there on any PC.
/// mpv is pointed at the folder (<c>sub-fonts-dir</c>); the preview loads the files by path, since they are not installed.
/// </summary>
internal static class BundledFonts
{
    private static readonly (string Family, string File)[] Fonts =
    [
        ("Inter", "Inter.ttf"),
        ("Lexend", "Lexend.ttf"),
        ("Atkinson Hyperlegible Next", "AtkinsonHyperlegibleNext.ttf"),
        ("Source Sans 3", "SourceSans3.ttf"),
    ];

    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");

    public static IEnumerable<string> Families => Fonts.Select(f => f.Family);

    /// <summary>The XAML font family for <paramref name="family"/>: a file reference for a bundled font, the name itself otherwise.</summary>
    public static string PreviewFamily(string family)
    {
        foreach ((string name, string file) in Fonts)
        {
            if (name.Equals(family.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return $"{new Uri(Path.Combine(Directory, file)).AbsoluteUri}#{name}";
            }
        }
        return family;
    }
}
