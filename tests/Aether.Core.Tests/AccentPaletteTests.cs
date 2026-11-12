using System.Xml.Linq;
using Aether.Core.Playback;
using Aether.Core.Tests.TestSupport;

namespace Aether.Core.Tests;

public class AccentPaletteTests
{
    [Theory]
    [InlineData("#22D3EE", 0x22, 0xD3, 0xEE)]
    [InlineData("22d3ee", 0x22, 0xD3, 0xEE)]
    [InlineData(" #2de ", 0x22, 0xDD, 0xEE)]
    public void Hex_Parses_With_Or_Without_Hash_Short_Or_Long(string text, byte r, byte g, byte b)
    {
        Assert.True(AccentPalette.TryParseHex(text, out AccentColor color));
        Assert.Equal(new AccentColor(r, g, b), color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGHHII")]
    [InlineData("#22D3EEFF")]
    public void Bad_Hex_Is_Rejected(string text)
    {
        Assert.False(AccentPalette.TryParseHex(text, out _));
    }

    [Fact]
    public void Normalize_Gives_The_Stored_Form()
    {
        Assert.Equal("#22D3EE", AccentPalette.Normalize("22d3ee"));
        Assert.Equal("#22DDEE", AccentPalette.Normalize("#2de"));
        Assert.Equal("mint", AccentPalette.Normalize("Mint"));
        Assert.Equal("none", AccentPalette.Normalize(" none "));
        Assert.Null(AccentPalette.Normalize("teal-ish"));
    }

    [Fact]
    public void None_Resolves_To_No_Colour_And_Presets_And_Hex_To_Theirs()
    {
        Assert.Null(AccentPalette.Resolve(AccentPalette.None));
        Assert.Null(AccentPalette.Resolve("bogus"));
        Assert.Equal(AccentPalette.Presets[0].Color, AccentPalette.Resolve(AccentPalette.Presets[0].Id));
        Assert.Equal(new AccentColor(0x12, 0x34, 0x56), AccentPalette.Resolve("#123456"));
    }

    [Fact]
    public void There_Are_Six_Presets_With_Distinct_Ids_And_Colours()
    {
        Assert.Equal(6, AccentPalette.Presets.Count);
        Assert.Equal(6, AccentPalette.Presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(6, AccentPalette.Presets.Select(p => p.Color).Distinct().Count());
        Assert.All(AccentPalette.Presets, p => Assert.True(AccentPalette.IsValid(p.Id)));
    }

    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF, true)]
    [InlineData(0x22, 0xD3, 0xEE, true)]
    [InlineData(0x10, 0x10, 0x10, false)]
    [InlineData(0x3B, 0x2A, 0x8C, false)]
    public void Text_On_The_Accent_Is_Dark_On_Light_Colours(byte r, byte g, byte b, bool darkText)
    {
        Assert.Equal(darkText, AccentPalette.WantsDarkText(new AccentColor(r, g, b)));
    }

    [Fact]
    public void Overrides_Fill_The_Switches_Opaque_And_Tint_The_Emphasis_Fills()
    {
        var accent = new AccentColor(0x22, 0xD3, 0xEE);
        IReadOnlyDictionary<string, uint> overrides = AccentPalette.Overrides(accent);

        Assert.Equal(0xFF22D3EEu, overrides["AccentFillColorDefaultBrush"]);
        Assert.Equal(0xFF22D3EEu, overrides["ToggleSwitchFillOn"]);
        Assert.Equal(0xFF0E1014u, overrides["ToggleSwitchKnobFillOn"]);
        Assert.Equal(0xFF0E1014u, overrides["TextOnAccentFillColorPrimaryBrush"]);
        Assert.All(overrides.Where(o => o.Key.StartsWith("AetherFillEmphasis", StringComparison.Ordinal)),
            o => Assert.True(o.Value >> 24 is > 0 and < 0xFF, o.Key));
        Assert.All(overrides.Where(o => !o.Key.Contains("Knob", StringComparison.Ordinal) && !o.Key.StartsWith("TextOnAccent", StringComparison.Ordinal)),
            o => Assert.Equal(0x22D3EEu, o.Value & 0xFFFFFF));
    }

    [Fact]
    public void Dark_Accent_Gets_A_White_Knob()
    {
        IReadOnlyDictionary<string, uint> overrides = AccentPalette.Overrides(new AccentColor(0x3B, 0x2A, 0x8C));

        Assert.Equal(0xFFFFFFFFu, overrides["ToggleSwitchKnobFillOn"]);
    }

    [Fact]
    public void Settings_Shows_A_Swatch_For_None_And_Every_Preset()
    {
        var page = XDocument.Load(RepoPath.Get("src", "Aether.App", "Settings", "Pages", "GeneralPage.xaml"));
        var tags = page.Descendants().Where(e => e.Name.LocalName == "ListBoxItem").Select(e => (string?)e.Attribute("Tag")).ToList();

        Assert.Equal([AccentPalette.None, .. AccentPalette.Presets.Select(p => p.Id)], tags);
    }
}
