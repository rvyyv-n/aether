using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace Aether.App.UiTests;

[Trait("Category", "UI")]
public sealed class ControlsAndNavigationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "aether-uitests-controls-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        AetherAppSession.StopExistingInstances();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void SpaceKey_TogglesPlayPause_AndSeekBarDrag_AdvancesPosition()
    {
        string sampleVideo = AetherAppSession.CreateSampleVideo(_tempDir, durationSeconds: 12);

        using AetherAppSession session = AetherAppSession.Launch(sampleVideo, dismissTourIfShown: false);
        session.FocusMainWindow();
        session.RevealTransportChrome();

        // Wait until the video is actively playing ("Pause (Space)").
        bool playing = Retry.WhileFalse(
            () =>
            {
                AutomationElement? btn = session.FindElement("PlayPauseButton");
                return btn?.Name?.StartsWith("Pause", StringComparison.OrdinalIgnoreCase) == true;
            },
            timeout: TimeSpan.FromSeconds(12),
            interval: TimeSpan.FromMilliseconds(150)).Success;
        Assert.True(playing, "Video did not reach playing state.");

        // Press Space to pause.
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.SPACE);
        bool paused = Retry.WhileFalse(
            () =>
            {
                AutomationElement? btn = session.FindElement("PlayPauseButton");
                return btn?.Name?.StartsWith("Play", StringComparison.OrdinalIgnoreCase) == true;
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(100)).Success;
        Assert.True(paused, "Space key did not pause playback.");

        // Press Space again to resume.
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.SPACE);
        bool resumed = Retry.WhileFalse(
            () =>
            {
                AutomationElement? btn = session.FindElement("PlayPauseButton");
                return btn?.Name?.StartsWith("Pause", StringComparison.OrdinalIgnoreCase) == true;
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(100)).Success;
        Assert.True(resumed, "Second Space key press did not resume playback.");

        // Pause again before dragging the seek bar so position assertions are deterministic.
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.SPACE);
        Retry.WhileFalse(
            () => session.FindElement("PlayPauseButton")?.Name?.StartsWith("Play", StringComparison.OrdinalIgnoreCase) == true,
            TimeSpan.FromSeconds(4));

        session.RevealTransportChrome();
        AutomationElement? seekBar = session.WaitForElement("SeekBar", TimeSpan.FromSeconds(5));
        Assert.NotNull(seekBar);

        Rectangle barRect = seekBar!.BoundingRectangle;
        Assert.True(barRect.Width > 40 && barRect.Height > 0, $"Unexpected SeekBar bounds: {barRect}.");

        int centerY = barRect.Top + barRect.Height / 2;
        var dragStart = new Point(barRect.Left + (int)(barRect.Width * 0.15), centerY);
        var dragEnd = new Point(barRect.Left + (int)(barRect.Width * 0.72), centerY);

        DesktopInput.Drag(session.MainWindow, dragStart, dragEnd);

        bool seeked = Retry.WhileFalse(
            () =>
            {
                AutomationElement? pos = session.FindElement("PositionText");
                return AetherAppSession.ParseClockSeconds(pos?.Name) >= 5.0;
            },
            timeout: TimeSpan.FromSeconds(5),
            interval: TimeSpan.FromMilliseconds(120)).Success;
        Assert.True(seeked, $"Seek bar drag did not advance PositionText to >= 5s (current: '{session.FindElement("PositionText")?.Name}').");

        Assert.True(session.CloseCleanly());
    }

    [Fact]
    public void CtrlComma_OpensSettings_NavigatesSections_AndEscapeCloses()
    {
        using AetherAppSession session = AetherAppSession.Launch();
        Assert.NotNull(session.WaitForElement("OpenFileButton", TimeSpan.FromSeconds(8)));
        session.FocusMainWindow();

        // Open Settings via Ctrl+,
        DesktopInput.PressChord(session.MainWindow, VirtualKeyShort.CONTROL, VirtualKeyShort.OEM_COMMA);

        AutomationElement? sectionList = session.WaitForElement("SectionList", TimeSpan.FromSeconds(6));
        Assert.NotNull(sectionList);

        AutomationElement? pageTitle = session.WaitForElement("PageTitle", TimeSpan.FromSeconds(4));
        Assert.NotNull(pageTitle);
        Assert.Equal("General", pageTitle!.Name);

        foreach (string targetSection in new[] { "Playback", "Audio", "Shortcuts", "About" })
        {
            AutomationElement? navItem = Retry.WhileNull(
                () => sectionList!.FindFirstDescendant(session.Cf.ByName(targetSection)),
                timeout: TimeSpan.FromSeconds(4),
                interval: TimeSpan.FromMilliseconds(100)).Result;
            Assert.NotNull(navItem);

            if (navItem!.Patterns.SelectionItem.IsSupported)
            {
                navItem.Patterns.SelectionItem.Pattern.Select();
            }
            else if (navItem.Parent?.Patterns.SelectionItem.IsSupported == true)
            {
                navItem.Parent.Patterns.SelectionItem.Pattern.Select();
            }
            else
            {
                navItem.Click();
            }

            bool navigated = Retry.WhileFalse(
                () => string.Equals(session.FindElement("PageTitle")?.Name, targetSection, StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(4),
                interval: TimeSpan.FromMilliseconds(100)).Success;
            Assert.True(navigated, $"Settings did not navigate to '{targetSection}' (PageTitle: '{session.FindElement("PageTitle")?.Name}').");
        }

        // Escape closes Settings and returns to Home.
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.ESCAPE);
        Assert.True(session.WaitUntilGone("SectionList", TimeSpan.FromSeconds(5)), "Settings did not close on Escape.");
        Assert.NotNull(session.WaitForElement("OpenFileButton", TimeSpan.FromSeconds(4)));

        Assert.True(session.CloseCleanly());
    }

    [Fact]
    public void RightClick_DisplaysContextMenuFlyout_AndDismissesWithEscape()
    {
        using AetherAppSession session = AetherAppSession.Launch();
        Assert.NotNull(session.WaitForElement("OpenFileButton", TimeSpan.FromSeconds(8)));
        session.FocusMainWindow();

        Rectangle bounds = session.MainWindow.BoundingRectangle;
        // Open the page context menu via right-click on interactive desktops (or context-menu key fallback when SendInput is unavailable).
        var target = new Point(bounds.Left + 80, bounds.Top + 110);
        DesktopInput.OpenContextMenu(session.MainWindow, target);

        AutomationElement? settingsItem = session.WaitForElement("CtxSettings", TimeSpan.FromSeconds(5), searchDesktopPopups: true);
        Assert.NotNull(settingsItem);

        AutomationElement? openFileItem = session.WaitForElement("CtxOpenFile", TimeSpan.FromSeconds(2), searchDesktopPopups: true);
        Assert.NotNull(openFileItem);

        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.ESCAPE);
        Assert.True(session.WaitUntilGone("CtxSettings", TimeSpan.FromSeconds(4), searchDesktopPopups: true),
            "Context menu flyout did not dismiss on Escape.");

        Assert.True(session.CloseCleanly());
    }
}
