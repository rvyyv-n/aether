using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class ResourcesStringsTests
{
    private static readonly XNamespace XNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static XDocument LoadResourcesResw()
    {
        string path = RepoPath.Get("src", "Aether.App", "Strings", "en-us", "Resources.resw");
        Assert.True(File.Exists(path), $"Resources.resw not found at: {path}");
        return XDocument.Load(path);
    }

    private static List<XElement> GetDataElements(XDocument doc) =>
        doc.Root?.Elements("data").ToList() ?? [];

    [Fact]
    public void Every_Resource_Name_Is_Unique()
    {
        var doc = LoadResourcesResw();
        var dataElements = GetDataElements(doc);

        Assert.NotEmpty(dataElements);

        var names = dataElements
            .Select(d => (string?)d.Attribute("name"))
            .Where(n => !string.IsNullOrEmpty(n))
            .Cast<string>()
            .ToList();

        Assert.Equal(dataElements.Count, names.Count);

        var duplicates = names
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void No_Resource_Value_Is_Empty_Or_Whitespace()
    {
        var doc = LoadResourcesResw();
        var dataElements = GetDataElements(doc);

        Assert.NotEmpty(dataElements);

        var emptyKeys = new List<string>();
        foreach (var data in dataElements)
        {
            string? name = (string?)data.Attribute("name");
            string? value = data.Element("value")?.Value;
            if (string.IsNullOrWhiteSpace(value))
            {
                emptyKeys.Add(name ?? "<unnamed>");
            }
        }

        Assert.Empty(emptyKeys);
    }

    [Fact]
    public void Every_Resource_Key_Follows_Area_Name_Convention()
    {
        var doc = LoadResourcesResw();
        var dataElements = GetDataElements(doc);

        Assert.NotEmpty(dataElements);

        // Keys follow Area_Name format (PascalCase area prefix followed by underscore and name part)
        var pattern = new Regex(@"^[A-Z][A-Za-z0-9]*_[A-Z][A-Za-z0-9_]*$", RegexOptions.Compiled);
        var invalidKeys = new List<string>();

        foreach (var data in dataElements)
        {
            string name = (string)data.Attribute("name")!;
            if (!pattern.IsMatch(name))
            {
                invalidKeys.Add(name);
            }
        }

        Assert.Empty(invalidKeys);
    }

    [Fact]
    public void Shortcut_Texts_Use_Ctrl_Plus_Format_Without_Spaces_Or_Dashes()
    {
        var doc = LoadResourcesResw();
        var dataElements = GetDataElements(doc);

        Assert.NotEmpty(dataElements);

        // Disallow 'Ctrl ' or 'Ctrl-'
        var badShortcutPattern = new Regex(@"\bCtrl[\s\-]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var violations = new List<string>();

        foreach (var data in dataElements)
        {
            string name = (string)data.Attribute("name")!;
            string value = data.Element("value")?.Value ?? string.Empty;

            if (badShortcutPattern.IsMatch(value))
            {
                violations.Add($"{name}: {value}");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Every_Xaml_Uid_Has_Matching_Resw_Key_Prefix()
    {
        var doc = LoadResourcesResw();
        var reswKeys = GetDataElements(doc)
            .Select(d => (string)d.Attribute("name")!)
            .ToHashSet(StringComparer.Ordinal);

        string appDir = RepoPath.Get("src", "Aether.App");
        var xamlFiles = Directory.GetFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                        !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .ToList();

        var uids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var xamlFile in xamlFiles)
        {
            var xamlDoc = XDocument.Load(xamlFile);
            var fileUids = xamlDoc.Descendants()
                .Select(e => (string?)e.Attribute(XNamespace + "Uid"))
                .Where(u => !string.IsNullOrEmpty(u))
                .Cast<string>();

            foreach (var uid in fileUids)
            {
                uids.Add(uid);
            }
        }

        // Skip if none are used yet in XAML files
        if (uids.Count == 0)
        {
            return;
        }

        var missingUids = uids
            .Where(uid => !reswKeys.Any(k => k.StartsWith($"{uid}_", StringComparison.Ordinal) || k.StartsWith($"{uid}.", StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(missingUids);
    }
}
