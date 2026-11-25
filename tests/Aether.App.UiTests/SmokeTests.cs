using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace Aether.App.UiTests;

[Trait("Category", "UI")]
public sealed class SmokeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "aether-uitests-smoke-" + Guid.NewGuid().ToString("N"));

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
    public void ColdStart_WindowMaximizeMinimizeRestore_AndCleanExit()
    {
        using AetherAppSession session = AetherAppSession.Launch();
        output.WriteLine($"Cold start to main window: {session.StartupElapsed.TotalMilliseconds:F0} ms");

        Assert.True(session.StartupElapsed < TimeSpan.FromSeconds(15),
            $"Cold start took {session.StartupElapsed.TotalMilliseconds:F0} ms.");
        Assert.NotNull(session.WaitForElement("OpenFileButton", TimeSpan.FromSeconds(8)));

        var windowPattern = session.MainWindow.Patterns.Window.Pattern;
        Assert.NotNull(windowPattern);

        // Maximize via custom caption button
        AutomationElement? maxButton = session.WaitForElement("CaptionMaximize", TimeSpan.FromSeconds(5));
        Assert.NotNull(maxButton);
        maxButton!.AsButton().Invoke();
        Assert.True(
            Retry.WhileFalse(
                () => windowPattern.WindowVisualState.Value == WindowVisualState.Maximized,
                TimeSpan.FromSeconds(4)).Success,
            "Window did not enter Maximized state.");

        // Restore via custom caption button
        maxButton.AsButton().Invoke();
        Assert.True(
            Retry.WhileFalse(
                () => windowPattern.WindowVisualState.Value == WindowVisualState.Normal,
                TimeSpan.FromSeconds(4)).Success,
            "Window did not return to Normal state from Maximized.");

        // Minimize via custom caption button, then restore via WindowPattern
        AutomationElement? minButton = session.WaitForElement("CaptionMinimize", TimeSpan.FromSeconds(5));
        Assert.NotNull(minButton);
        minButton!.AsButton().Invoke();
        Assert.True(
            Retry.WhileFalse(
                () => windowPattern.WindowVisualState.Value == WindowVisualState.Minimized,
                TimeSpan.FromSeconds(4)).Success,
            "Window did not enter Minimized state.");

        windowPattern.SetWindowVisualState(WindowVisualState.Normal);
        Assert.True(
            Retry.WhileFalse(
                () => windowPattern.WindowVisualState.Value == WindowVisualState.Normal,
                TimeSpan.FromSeconds(4)).Success,
            "Window did not restore from Minimized state.");

        Assert.True(session.CloseCleanly(), "Aether.App did not exit cleanly with exit code 0.");
    }

    [Fact]
    public void LaunchWithVideoArgument_StartsPlaybackAndShowsTransport()
    {
        string sampleVideo = AetherAppSession.CreateSampleVideo(_tempDir, durationSeconds: 8);

        using AetherAppSession session = AetherAppSession.Launch(sampleVideo, dismissTourIfShown: false);
        session.FocusMainWindow();
        session.RevealTransportChrome();

        AutomationElement? playPause = Retry.WhileNull(
            () =>
            {
                AutomationElement? button = session.FindElement("PlayPauseButton");
                if (button is null)
                {
                    return null;
                }
                string name = button.Name ?? string.Empty;
                return name.StartsWith("Pause", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("Play", StringComparison.OrdinalIgnoreCase)
                    ? button
                    : null;
            },
            timeout: TimeSpan.FromSeconds(12),
            interval: TimeSpan.FromMilliseconds(150)).Result;

        Assert.NotNull(playPause);

        AutomationElement? seekBar = session.WaitForElement("SeekBar", TimeSpan.FromSeconds(5));
        Assert.NotNull(seekBar);

        AutomationElement? duration = Retry.WhileNull(
            () =>
            {
                AutomationElement? el = session.FindElement("DurationText");
                return el is not null && AetherAppSession.ParseClockSeconds(el.Name) >= 6 ? el : null;
            },
            timeout: TimeSpan.FromSeconds(10),
            interval: TimeSpan.FromMilliseconds(150)).Result;
        Assert.NotNull(duration);

        Assert.True(session.CloseCleanly(), "Aether.App did not exit cleanly after video launch.");
    }
}
