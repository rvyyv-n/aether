using System.Xml.Linq;
using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class LookModeTests
{
    [Fact]
    public void LookModes_RecognisesOnlyAetherAndNative()
    {
        Assert.True(LookModes.IsValid(LookModes.Aether));
        Assert.True(LookModes.IsValid(LookModes.Native));
        Assert.False(LookModes.IsValid("fluent"));
        Assert.False(LookModes.IsValid(null));
        Assert.True(LookModes.IsNative(LookModes.Native));
        Assert.False(LookModes.IsNative(LookModes.Aether));
    }

    [Fact]
    public void CornerRadii_SwitchBetweenAetherPillGlassAndNativeStandard8Px()
    {
        LookCornerRadii aether = LookCornerRadii.For(LookModes.Aether);
        LookCornerRadii native = LookCornerRadii.For(LookModes.Native);

        Assert.Equal(24, aether.Transport);
        Assert.Equal(18, aether.PlayButton);
        Assert.Equal(16, aether.Tile);
        Assert.Equal(6, aether.TransportButton);

        Assert.Equal(8, native.Transport);
        Assert.Equal(8, native.PlayButton);
        Assert.Equal(8, native.SidebarItem);
        Assert.Equal(8, native.Tile);
        Assert.Equal(8, native.SidePanel);
        Assert.Equal(8, native.ClipCard);
        Assert.Equal(8, native.Overlay);
        Assert.Equal(8, native.TransportButton);
        Assert.Equal(4, native.Control);
    }

    [Fact]
    public void ResolveEffective_KeepsNoneMonochromeInBothAetherAndNativeLook()
    {
        var system = new AccentColor(0x00, 0x78, 0xD4);

        Assert.Null(AccentPalette.ResolveEffective(AccentPalette.None, nativeLook: false, system));
        Assert.Null(AccentPalette.ResolveEffective(AccentPalette.None, nativeLook: true, system));

        AccentColor explicitPreset = AccentPalette.Resolve("mint")!.Value;
        Assert.Equal(explicitPreset, AccentPalette.ResolveEffective("mint", nativeLook: true, system));
        Assert.Equal(new AccentColor(0xFF, 0x11, 0x22), AccentPalette.ResolveEffective("#FF1122", nativeLook: true, system));
    }

    [Fact]
    public void OledBrushOverrides_ResolveInDarkTheme_AndDoNotOverlapAccentOverrides()
    {
        var accentKeys = AccentPalette.Overrides(new AccentColor(0x22, 0xD3, 0xEE)).Keys.ToHashSet(StringComparer.Ordinal);
        foreach (string key in ThemeModes.OledBrushOverrides.Keys)
        {
            Assert.DoesNotContain(key, accentKeys);
        }

        XNamespace xamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        var doc = XDocument.Load(RepoPath.Get("src", "Aether.App", "Theme", "DesignTokens.xaml"));
        XElement darkDict = doc.Descendants(xamlNs + "ResourceDictionary")
            .Single(d => (string?)d.Attribute(xNs + "Key") == "Dark");
        var darkBrushKeys = darkDict.Elements(xamlNs + "SolidColorBrush")
            .Select(e => (string)e.Attribute(xNs + "Key")!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(ThemeModes.OledBrushOverrides.Keys, key => Assert.Contains(key, darkBrushKeys));
        Assert.Equal(0xFF000000u, ThemeModes.OledBrushOverrides["AetherSurfaceLayerBrush"]);
        Assert.Equal(ThemeModes.OledBrushOverrides["AetherSurfaceBarBrush"], ThemeModes.OledBrushOverrides["AetherSurfaceMenuBrush"]);
        Assert.Equal(ThemeModes.OledBrushOverrides["AetherStrokeBarBrush"], ThemeModes.OledBrushOverrides["AetherStrokeMenuBrush"]);
    }

    [Fact]
    public void SettingsStore_PersistsLookModeAndRejectsInvalidValues()
    {
        var storage = new MemorySettingsStorage();
        var store = new SettingsStore(storage);

        Assert.Equal(LookModes.Aether, store.Current.LookMode);
        Assert.True(store.Set(SettingKeys.LookMode, LookModes.Native));
        Assert.False(store.Set(SettingKeys.LookMode, "invalid"));

        store.Save();

        var reloaded = new SettingsStore(storage);
        reloaded.Load();
        Assert.Equal(LookModes.Native, reloaded.Current.LookMode);
    }

    private sealed class MemorySettingsStorage : ISettingsStorage
    {
        private string? _text;
        public string? Read() => _text;
        public void Write(string contents) => _text = contents;
    }
}
