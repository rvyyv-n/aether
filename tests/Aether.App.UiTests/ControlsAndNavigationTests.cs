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

        // Wait for the transport bar expand animation (including Springy ~500 ms settle) to finish before measuring.
        Rectangle barRect = Rectangle.Empty;
        Retry.WhileFalse(
            () =>
            {
                Rectangle first = seekBar!.BoundingRectangle;
                Thread.Sleep(120);
                Rectangle second = seekBar.BoundingRectangle;
                barRect = second;
                return second.Width > 120 && second.Height > 0 && first == second;
            },
            timeout: TimeSpan.FromSeconds(3),
            interval: TimeSpan.FromMilliseconds(80));
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

        // Open the transport Open, Speed, and More menus and verify each sits cleanly above the seek row.
        Rectangle seekRect = seekBar.BoundingRectangle;

        AutomationElement? openBtn = session.WaitForElement("OpenButton", TimeSpan.FromSeconds(4));
        Assert.NotNull(openBtn);
        openBtn!.AsButton().Invoke();
        AutomationElement? openUrlItem = session.WaitForElement("OpenMenuUrlItem", TimeSpan.FromSeconds(4), searchDesktopPopups: true);
        Assert.NotNull(openUrlItem);
        Assert.True(openUrlItem!.BoundingRectangle.Bottom <= seekRect.Top + 4,
            $"OpenMenu overlapped the seek row (menu bottom {openUrlItem.BoundingRectangle.Bottom} > seek top {seekRect.Top}).");
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.ESCAPE);
        session.WaitUntilGone("OpenMenuUrlItem", TimeSpan.FromSeconds(3), searchDesktopPopups: true);

        AutomationElement? moreBtn = session.WaitForElement("MoreButton", TimeSpan.FromSeconds(4));
        Assert.NotNull(moreBtn);
        moreBtn!.AsButton().Invoke();
        AutomationElement? moreSettingsItem = session.WaitForElement("MoreSettingsItem", TimeSpan.FromSeconds(4), searchDesktopPopups: true);
        Assert.NotNull(moreSettingsItem);
        Assert.True(moreSettingsItem!.BoundingRectangle.Bottom <= seekRect.Top + 4,
            $"MoreMenu overlapped the seek row (menu bottom {moreSettingsItem.BoundingRectangle.Bottom} > seek top {seekRect.Top}).");
        DesktopInput.PressKey(session.MainWindow, VirtualKeyShort.ESCAPE);
        session.WaitUntilGone("MoreSettingsItem", TimeSpan.FromSeconds(3), searchDesktopPopups: true);

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
        session.FocusMainWindow();
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

    [Fact]
    public void RailHandle_HoverAndClick_OpensSidebarRail_AndSettingsShortcutsResizeStaysInBounds()
    {
        using AetherAppSession session = AetherAppSession.Launch();
        Assert.NotNull(session.WaitForElement("OpenFileButton", TimeSpan.FromSeconds(8)));
        session.FocusMainWindow();

        // Approach left edge to reveal the dynamic chevron handle, then click it to open the floating sidebar pill.
        Rectangle winBounds = session.MainWindow.BoundingRectangle;
        var edgePoint = new Point(winBounds.Left + 12, winBounds.Top + winBounds.Height / 2);
        DesktopInput.MoveTo(session.MainWindow, new Point(winBounds.Left + 40, winBounds.Top + winBounds.Height / 2));
        DesktopInput.MoveTo(session.MainWindow, edgePoint);

        AutomationElement? handle = Retry.WhileNull(
            () =>
            {
                AutomationElement? el = session.FindElement("RailHandle");
                return el is not null && el.BoundingRectangle.Width > 0 && el.BoundingRectangle.Height > 0 ? el : null;
            },
            timeout: TimeSpan.FromSeconds(4),
            interval: TimeSpan.FromMilliseconds(100)).Result;
        Assert.NotNull(handle);

        Rectangle handleRect = handle!.BoundingRectangle;
        if (DesktopInput.IsHardwareInputAvailable)
        {
            DesktopInput.Click(session.MainWindow, new Point(handleRect.Left + handleRect.Width / 2, handleRect.Top + handleRect.Height / 2));
        }
        else
        {
            // Non-active desktops do not dispatch uncaptured hover WM_MOUSEMOVE into WinUI 3; invoke the handle directly.
            handle.AsButton().Invoke();
        }

        AutomationElement? railSettings = Retry.WhileNull(
            () =>
            {
                AutomationElement? el = session.FindElement("RailSettings");
                return el is not null && !el.IsOffscreen && el.BoundingRectangle.Width > 0 ? el : null;
            },
            timeout: TimeSpan.FromSeconds(4),
            interval: TimeSpan.FromMilliseconds(100)).Result;
        Assert.NotNull(railSettings);

        // Open Settings via the sidebar button, navigate to Shortcuts, maximize, then resize down and verify zero right-edge clipping.
        railSettings!.AsButton().Invoke();
        AutomationElement? sectionList = session.WaitForElement("SectionList", TimeSpan.FromSeconds(6));
        Assert.NotNull(sectionList);

        AutomationElement? shortcutsNav = Retry.WhileNull(
            () => sectionList!.FindFirstDescendant(session.Cf.ByName("Shortcuts")),
            timeout: TimeSpan.FromSeconds(4),
            interval: TimeSpan.FromMilliseconds(100)).Result;
        Assert.NotNull(shortcutsNav);
        if (shortcutsNav!.Patterns.SelectionItem.IsSupported)
        {
            shortcutsNav.Patterns.SelectionItem.Pattern.Select();
        }
        else if (shortcutsNav.Parent?.Patterns.SelectionItem.IsSupported == true)
        {
            shortcutsNav.Parent.Patterns.SelectionItem.Pattern.Select();
        }
        else
        {
            shortcutsNav.Click();
        }

        Assert.True(
            Retry.WhileFalse(
                () => string.Equals(session.FindElement("PageTitle")?.Name, "Shortcuts", StringComparison.Ordinal),
                timeout: TimeSpan.FromSeconds(4),
                interval: TimeSpan.FromMilliseconds(100)).Success,
            "Settings did not navigate to Shortcuts.");

        var windowPattern = session.MainWindow.Patterns.Window.Pattern;
        windowPattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Maximized);
        Thread.Sleep(250);
        windowPattern.SetWindowVisualState(FlaUI.Core.Definitions.WindowVisualState.Normal);

        foreach ((int width, int height) in new[] { (1315, 750), (1180, 740), (540, 700) })
        {
            DesktopInput.ResizeWindow(session.MainWindow, width, height);

            bool fitsAfterResize = Retry.WhileFalse(
                () =>
                {
                    AutomationElement? scroller = session.FindElement("ContentScroller");
                    AutomationElement? title = session.FindElement("PageTitle");
                    AutomationElement? playPauseRow = session.MainWindow.FindFirstDescendant(session.Cf.ByName("Change: Play or pause"));
                    if (scroller is null || title is null || playPauseRow is null)
                    {
                        return false;
                    }
                    Rectangle paneRect = scroller.BoundingRectangle;
                    Rectangle titleRect = title.BoundingRectangle;
                    Rectangle rowRect = playPauseRow.BoundingRectangle;
                    return paneRect.Width > 0
                        && titleRect.Left >= paneRect.Left
                        && titleRect.Right <= paneRect.Right
                        && rowRect.Width > 0
                        && rowRect.Left >= paneRect.Left
                        && rowRect.Right <= paneRect.Right;
                },
                timeout: TimeSpan.FromSeconds(4),
                interval: TimeSpan.FromMilliseconds(120)).Success;

            Rectangle finalWin = session.MainWindow.BoundingRectangle;
            AutomationElement? finalScroller = session.FindElement("ContentScroller");
            AutomationElement? finalTitle = session.FindElement("PageTitle");
            AutomationElement? finalRow = session.MainWindow.FindFirstDescendant(session.Cf.ByName("Change: Play or pause"));
            Assert.True(fitsAfterResize,
                $"Shortcuts clipped or drifted at {width}x{height}. win={finalWin}, scroller={finalScroller?.BoundingRectangle}, title={finalTitle?.BoundingRectangle}, row={finalRow?.BoundingRectangle}");
        }

        Assert.True(session.CloseCleanly());
    }
}
