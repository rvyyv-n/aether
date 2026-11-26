using System.Xml.Linq;
using Aether.Core.Playback.Windowing;
using Aether.Core.Tests.TestSupport;
using Xunit;

namespace Aether.Core.Tests;

public class FlyoutXamlTests
{
    // The page fades each flyout in itself, on the same tick and curve as the blur behind it (12.7). A flyout's own
    // transition runs on another clock, and Windows' acrylic on its popup window shows before its content.
    [Fact]
    public void Every_Player_Flyout_Has_Its_Own_Transition_And_Acrylic_Off()
    {
        var doc = XDocument.Load(RepoPath.Get("src", "Aether.App", "MainPage.xaml"));
        var flyouts = doc.Descendants().Where(e => e.Name.LocalName is "Flyout" or "MenuFlyout").ToList();

        Assert.NotEmpty(flyouts);
        var animated = flyouts
            .Where(f => (string?)f.Attribute("AreOpenCloseAnimationsEnabled") != "False"
                || (string?)f.Attribute("SystemBackdrop") != "{StaticResource NoPopupBackdrop}")
            .Select(f => (string?)f.Attribute("Opened") ?? f.ToString().Split('\n')[0])
            .ToList();

        Assert.Empty(animated);
    }

    [Theory]
    [InlineData(760, 32, 24, 88, 598)]
    [InlineData(760, 0, 24, 88, 630)]
    [InlineData(400, 32, 24, 88, 238)]
    [InlineData(1440, 32, 24, 88, 1278)]
    [InlineData(120, 32, 24, 88, 0)]
    public void MaxFlyoutHeightAboveBar_FitsBetweenTitleBarAndExpandedOsd(
        double windowHeight,
        double titleBarHeight,
        double barBottomMargin,
        double barFullHeight,
        double expected)
    {
        double actual = WindowPlacement.MaxFlyoutHeightAboveBar(
            windowHeight,
            titleBarHeight,
            barBottomMargin,
            barFullHeight);

        Assert.Equal(expected, actual);
    }
}
