using System.Xml.Linq;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class FlyoutXamlTests
{
    // The page fades each flyout in itself, on the same tick and curve as the blur behind it (12.7); a flyout's own
    // transition runs on another clock, so its blur showed and left a moment before it.
    [Fact]
    public void Every_Player_Flyout_Has_Its_Own_Transition_Off()
    {
        var doc = XDocument.Load(RepoPath.Get("src", "Aether.App", "MainPage.xaml"));
        var flyouts = doc.Descendants().Where(e => e.Name.LocalName is "Flyout" or "MenuFlyout").ToList();

        Assert.NotEmpty(flyouts);
        var animated = flyouts
            .Where(f => (string?)f.Attribute("AreOpenCloseAnimationsEnabled") != "False")
            .Select(f => (string?)f.Attribute("Opened") ?? f.ToString().Split('\n')[0])
            .ToList();

        Assert.Empty(animated);
    }
}
