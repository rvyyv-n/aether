using System.Diagnostics;
using System.Globalization;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Aether.App.UiTests;

/// <summary>
/// Manages one unpackaged <c>Aether.App.exe</c> test process and its UIA3 session, guaranteeing
/// pre-launch isolation (for single-instance handoff) and unconditional process cleanup on disposal.
/// </summary>
internal sealed class AetherAppSession : IDisposable
{
    private readonly UIA3Automation _automation;
    private readonly Process _process;
    private readonly Application _app;
    private bool _disposed;

    private AetherAppSession(UIA3Automation automation, Process process, Application app, Window mainWindow, TimeSpan startupElapsed)
    {
        _automation = automation;
        _process = process;
        _app = app;
        MainWindow = mainWindow;
        StartupElapsed = startupElapsed;
    }

    public UIA3Automation Automation => _automation;

    public Application App => _app;

    public Window MainWindow { get; }

    public TimeSpan StartupElapsed { get; }

    public ConditionFactory Cf => _automation.ConditionFactory;

    public static string LocateAppExecutable()
    {
        if (Environment.GetEnvironmentVariable("AETHER_UI_TEST_EXE") is { Length: > 0 } envExe && File.Exists(envExe))
        {
            return Path.GetFullPath(envExe);
        }

        string repoRoot = FindRepoRoot();
        bool isRelease = AppContext.BaseDirectory.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
        string[] configs = isRelease ? ["Release", "Debug"] : ["Debug", "Release"];
        string[] platforms = ["x64", "x86"];

        foreach (string platform in platforms)
        {
            string rid = platform == "x64" ? "win-x64" : "win-x86";
            foreach (string config in configs)
            {
                string candidate = Path.Combine(
                    repoRoot, "src", "Aether.App", "bin", platform, config, "net10.0-windows10.0.26100.0", rid, "Aether.App.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new FileNotFoundException(
            $"Could not find compiled unpackaged Aether.App.exe under '{repoRoot}'. Build src/Aether.App first or set AETHER_UI_TEST_EXE.");
    }

    public static AetherAppSession Launch(string? videoPath = null, bool dismissTourIfShown = true)
    {
        StopExistingInstances();

        string exePath = LocateAppExecutable();
        var psi = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false,
        };
        if (!string.IsNullOrWhiteSpace(videoPath))
        {
            psi.ArgumentList.Add(videoPath);
        }

        var automation = new UIA3Automation();
        var stopwatch = Stopwatch.StartNew();
        Process? process = null;
        Application? app = null;

        try
        {
            process = Process.Start(psi) ?? throw new InvalidOperationException($"Failed to start '{exePath}'.");
            app = Application.Attach(process.Id);
            Window? window = Retry.WhileNull(
                () =>
                {
                    try
                    {
                        return app.GetMainWindow(automation, TimeSpan.FromMilliseconds(500));
                    }
                    catch
                    {
                        return null;
                    }
                },
                timeout: TimeSpan.FromSeconds(15),
                interval: TimeSpan.FromMilliseconds(100)).Result;

            if (window is null)
            {
                throw new TimeoutException($"Timed out waiting for Aether main window after launching '{exePath}'.");
            }

            stopwatch.Stop();
            var session = new AetherAppSession(automation, process, app, window, stopwatch.Elapsed);

            if (dismissTourIfShown && string.IsNullOrWhiteSpace(videoPath))
            {
                session.DismissTourIfPresent();
            }

            return session;
        }
        catch
        {
            TryKill(process, app);
            automation.Dispose();
            throw;
        }
    }

    public AutomationElement? WaitForElement(string automationId, TimeSpan? timeout = null, bool searchDesktopPopups = false)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(8);
        return Retry.WhileNull(
            () => FindElement(automationId, searchDesktopPopups),
            timeout: limit,
            interval: TimeSpan.FromMilliseconds(100)).Result;
    }

    public AutomationElement? FindElement(string automationId, bool searchDesktopPopups = false)
    {
        try
        {
            AutomationElement? found = MainWindow.FindFirstDescendant(Cf.ByAutomationId(automationId));
            if (found is not null || !searchDesktopPopups)
            {
                return found;
            }

            int pid = _app.ProcessId;
            foreach (AutomationElement top in _automation.GetDesktop().FindAllChildren(Cf.ByProcessId(pid)))
            {
                AutomationElement? popupItem = top.FindFirstDescendant(Cf.ByAutomationId(automationId));
                if (popupItem is not null)
                {
                    return popupItem;
                }
            }
        }
        catch
        {
            // Transient COM/UIA refresh error while XAML tree updates.
        }
        return null;
    }

    public bool WaitUntilGone(string automationId, TimeSpan? timeout = null, bool searchDesktopPopups = false)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(5);
        return Retry.WhileTrue(
            () =>
            {
                AutomationElement? el = FindElement(automationId, searchDesktopPopups);
                return el is not null && !el.IsOffscreen;
            },
            timeout: limit,
            interval: TimeSpan.FromMilliseconds(100)).Success;
    }

    public void FocusMainWindow()
    {
        try
        {
            MainWindow.Focus();
            MainWindow.SetForeground();
        }
        catch
        {
        }
        Thread.Sleep(120);
    }

    public void RevealTransportChrome()
    {
        System.Drawing.Rectangle bounds = MainWindow.BoundingRectangle;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        // Move across two points inside the lower half of the window so MainPage detects pointer motion.
        var p1 = new System.Drawing.Point(bounds.Left + bounds.Width / 2 - 20, bounds.Bottom - 70);
        var p2 = new System.Drawing.Point(bounds.Left + bounds.Width / 2 + 20, bounds.Bottom - 70);
        DesktopInput.MoveTo(MainWindow, p1);
        Thread.Sleep(60);
        DesktopInput.MoveTo(MainWindow, p2);
        Thread.Sleep(180);
    }

    public bool CloseCleanly(TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(6);
        try
        {
            MainWindow.Close();
        }
        catch
        {
            try
            {
                if (FindElement("CaptionClose") is { } closeBtn)
                {
                    closeBtn.AsButton().Invoke();
                }
            }
            catch
            {
            }
        }

        if (!_process.WaitForExit((int)limit.TotalMilliseconds))
        {
            return false;
        }
        return _process.ExitCode == 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        TryKill(_process, _app);
        _automation.Dispose();
    }

    public static void StopExistingInstances()
    {
        foreach (Process proc in Process.GetProcessesByName("Aether.App"))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
            }
            catch
            {
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    public static string CreateSampleVideo(string directory, int durationSeconds = 12)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"ui-sample-{durationSeconds}s.mp4");
        if (File.Exists(path))
        {
            return path;
        }

        string ffmpeg = FindFfmpeg();
        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (string arg in new[]
        {
            "-v", "error", "-y",
            "-f", "lavfi", "-i", $"testsrc2=size=640x360:rate=30:duration={durationSeconds}",
            "-f", "lavfi", "-i", $"sine=frequency=440:duration={durationSeconds}",
            "-c:v", "libx264", "-preset", "ultrafast", "-g", "15", "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            path,
        })
        {
            psi.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ffmpeg.");
        string stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(20000) || process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed creating sample video (exit {process.ExitCode}): {stderr}");
        }
        return path;
    }

    public static double ParseClockSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return -1;
        }

        string clean = text.Trim().TrimStart('−', '-');
        string[] parts = clean.Split(':');
        if (parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s))
        {
            return m * 60 + s;
        }
        if (parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int m3)
            && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int s3))
        {
            return h * 3600 + m3 * 60 + s3;
        }
        return -1;
    }

    private void DismissTourIfPresent()
    {
        // On a fresh profile (e.g. CI VM), QueueLaunchTour starts its 400 ms timer only after libmpv finishes
        // initializing, so give it enough time to open before dismissing; once TourSeen is persisted, check briefly.
        bool tourAlreadySeen = IsTourAlreadySeen();
        if (!tourAlreadySeen)
        {
            WaitForElement("OpenFileButton", TimeSpan.FromSeconds(8));
        }

        TimeSpan wait = tourAlreadySeen ? TimeSpan.FromMilliseconds(350) : TimeSpan.FromSeconds(4);
        AutomationElement? skip = WaitForElement("TourSkipButton", wait);
        if (skip is not null)
        {
            try
            {
                skip.AsButton().Invoke();
            }
            catch
            {
                DesktopInput.PressKey(MainWindow, VirtualKeyShort.ESCAPE);
            }
            WaitUntilGone("TourSkipButton", TimeSpan.FromSeconds(3));
        }
    }

    private static bool IsTourAlreadySeen()
    {
        try
        {
            string settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aether",
                "settings.json");
            if (!File.Exists(settingsPath))
            {
                return false;
            }
            string json = File.ReadAllText(settingsPath);
            return json.Contains("\"tourSeen\": true", StringComparison.OrdinalIgnoreCase)
                || json.Contains("\"tourSeen\":true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FindFfmpeg()
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        return "ffmpeg.exe";
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AetherPlayer.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException($"Could not locate AetherPlayer.sln above '{AppContext.BaseDirectory}'.");
    }

    private static void TryKill(Process? process, Application? app)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                try
                {
                    app?.Close();
                }
                catch
                {
                }
                if (!process.WaitForExit(2000))
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(3000);
                }
            }
        }
        catch
        {
            try
            {
                app?.Kill();
            }
            catch
            {
            }
        }
        finally
        {
            app?.Dispose();
            process?.Dispose();
        }
    }
}
