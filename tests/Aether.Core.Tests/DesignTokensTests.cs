using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class DesignTokensTests
{
    private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace XNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> KnownBuiltIns =
    [
        "SymbolThemeFontFamily",
        "DefaultTextBoxStyle",
        "DefaultMenuFlyoutItemStyle",
        "DefaultToggleMenuFlyoutItemStyle"
    ];

    private static readonly string[] LegacyAliasKeys =
    [
        "AetherPanelColor",
        "AetherPanelBrush",
        "AetherOsdColor",
        "AetherOsdBrush",
        "AetherStrokeColor",
        "AetherStrokeBrush",
        "AetherStrokeStrongColor",
        "AetherStrokeStrongBrush",
        "AetherSelectedColor",
        "AetherSelectedBrush",
        "AetherActiveColor",
        "AetherActiveBrush",
        "AetherHoverColor",
        "AetherHoverBrush",
        "AetherPressedColor",
        "AetherPressedBrush",
        "AetherSuccessColor",
        "AetherSuccessBrush",
        "AetherWarningColor",
        "AetherWarningBrush",
        "AetherNeutralDotColor",
        "AetherNeutralDotBrush",
        "AetherLetterboxFullScreenColor",
        "AetherLetterboxFullScreenBrush",
        "AetherTitleFontSize",
        "AetherTitleBarHeight",
        "AetherPanelCornerRadius",
        "AetherBadgeCornerRadius",
        "AetherChromeShowMilliseconds",
        "AetherChromeHideMilliseconds",
        "AetherMonoTextStyle",
        "AetherTitleTextStyle",
        "AetherPanelStyle",
        "AetherPillButtonStyle",
        "AetherSelectedPillButtonStyle"
    ];

    private static XDocument LoadDesignTokens()
    {
        string path = RepoPath.Get("src", "Aether.App", "Theme", "DesignTokens.xaml");
        Assert.True(File.Exists(path), $"DesignTokens.xaml not found at: {path}");
        return XDocument.Load(path);
    }

    private static List<string> ExtractAllKeys(XDocument doc) =>
        doc.Descendants()
            .Select(e => (string?)e.Attribute(XNamespace + "Key"))
            .Where(k => !string.IsNullOrEmpty(k))
            .Cast<string>()
            .ToList();

    [Fact]
    public void Every_Key_Is_Unique()
    {
        var doc = LoadDesignTokens();
        var keys = ExtractAllKeys(doc);

        Assert.NotEmpty(keys);

        var duplicates = keys
            .GroupBy(k => k, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Every_Color_Has_A_Matching_Brush()
    {
        var doc = LoadDesignTokens();
        var allKeys = ExtractAllKeys(doc).ToHashSet(StringComparer.Ordinal);

        var colorKeys = doc.Descendants()
            .Where(e => e.Name.LocalName == "Color" || (e.Attribute(XNamespace + "Key")?.Value.EndsWith("Color", StringComparison.Ordinal) == true))
            .Select(e => (string)e.Attribute(XNamespace + "Key")!)
            .Where(k => k.StartsWith("Aether", StringComparison.Ordinal) && k.EndsWith("Color", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(colorKeys);

        foreach (var colorKey in colorKeys)
        {
            string expectedBrushKey = colorKey[..^5] + "Brush";
            Assert.Contains(expectedBrushKey, allKeys);
        }
    }

    [Fact]
    public void Every_Resource_Reference_In_App_Xaml_Resolves_To_Token_Or_BuiltIn()
    {
        var doc = LoadDesignTokens();
        var tokenKeys = ExtractAllKeys(doc).ToHashSet(StringComparer.Ordinal);

        string appDir = RepoPath.Get("src", "Aether.App");
        var xamlFiles = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        Assert.NotEmpty(xamlFiles);

        var resourceRegex = new Regex(@"\{(?:StaticResource|ThemeResource)\s+([A-Za-z0-9_]+)\}", RegexOptions.Compiled);
        var localKeyRegex = new Regex(@"x:Key=""([A-Za-z0-9_]+)""", RegexOptions.Compiled);
        var unresolved = new List<string>();

        foreach (var xamlFile in xamlFiles)
        {
            string content = File.ReadAllText(xamlFile);
            // Resources a view declares for itself (item styles, templates) resolve within that file.
            var localKeys = localKeyRegex.Matches(content).Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            var matches = resourceRegex.Matches(content);
            foreach (Match match in matches)
            {
                string key = match.Groups[1].Value;
                if (!tokenKeys.Contains(key) && !KnownBuiltIns.Contains(key) && !localKeys.Contains(key))
                {
                    unresolved.Add($"{Path.GetFileName(xamlFile)} -> {key}");
                }
            }
        }

        Assert.Empty(unresolved);
    }

    [Fact]
    public void Every_Legacy_Alias_Key_Exists()
    {
        var doc = LoadDesignTokens();
        var allKeys = ExtractAllKeys(doc).ToHashSet(StringComparer.Ordinal);

        var missing = LegacyAliasKeys
            .Where(alias => !allKeys.Contains(alias))
            .ToList();

        Assert.Empty(missing);
    }
}
