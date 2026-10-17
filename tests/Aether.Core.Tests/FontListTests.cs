using Aether.Core.Playback.Settings;

namespace Aether.Core.Tests;

public class FontListTests
{
    [Fact]
    public void Build_SortsAndRemovesDuplicatesIgnoringCase()
    {
        List<string> list = FontList.Build(["Verdana", "arial", "Arial", "  ", "Bricolage Grotesque"], "Verdana");

        Assert.Equal(["arial", "Bricolage Grotesque", "Verdana"], list);
    }

    [Fact]
    public void Build_KeepsTheCurrentFontEvenWhenNotInstalled()
    {
        List<string> list = FontList.Build(["Arial"], "My Missing Font");

        Assert.Contains("My Missing Font", list);
    }

    [Fact]
    public void Filter_PutsPrefixMatchesFirst()
    {
        List<string> result = FontList.Filter(["Segoe UI", "Open Sans", "Sans Forgetica", "Arial"], "sans");

        Assert.Equal(["Sans Forgetica", "Open Sans"], result);
    }

    [Fact]
    public void Filter_BlankReturnsEverything()
    {
        Assert.Equal(["A", "B"], FontList.Filter(["A", "B"], " "));
    }
}
