using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Resume;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Windowing;
using Aether.PvP.Review.Clipping;
using Aether_App.Input;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Aether_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    // The longest the first window waits for the settings and window files before using the default size.
    private static readonly TimeSpan RestoreWait = TimeSpan.FromMilliseconds(250);

    private static readonly object RedirectGate = new();
    private static MainWindow? s_window;
    private static DispatcherQueue? s_queue;
    private static string? s_earlyPath; // a file handed over before the window existed
    private static bool s_earlyActivation;

    private Window? _window;

    /// <summary>The single settings instance. Loaded on the thread pool at launch; it holds defaults until then.</summary>
    public static SettingsStore Settings { get; } = new(new FileSettingsStorage(FileSettingsStorage.DefaultPath()));

    /// <summary>The configurable shortcut keys, kept in <see cref="Settings"/>.</summary>
    public static ShortcutSettings Shortcuts { get; } = new(Settings);

    /// <summary>Completes once saved settings are read; engine start waits on it so start-only options use the saved values.</summary>
    public static Task SettingsLoaded { get; private set; } = Task.CompletedTask;

    /// <summary>Recently opened files for the empty state. Loaded on the thread pool at launch; empty until then.</summary>
    public static RecentsStore Recents { get; } = new(new FileSettingsStorage(RecentsStore.DefaultPath()));

    /// <summary>Where each file was left, for Resume playback. Loaded on the thread pool at launch; empty until then.</summary>
    public static ResumeStore Resume { get; } = new(new FileSettingsStorage(ResumeStore.DefaultPath()));

    /// <summary>Exported clips for the clip drawer. Loaded on the thread pool at launch; empty until then.</summary>
    public static ClipShelf Clips { get; } = new(new FileSettingsStorage(ClipShelf.DefaultPath()));

    /// <summary>Where the main window was left, for Remember window size. Loaded on the thread pool at launch.</summary>
    public static WindowStateStore WindowState { get; } = new(new FileSettingsStorage(WindowStateStore.DefaultPath()));

    /// <summary>Lossless clip export through FFmpeg on the PATH, pre-warmed by the clip drawer once the page is idle.</summary>
    public static ClipExportService ClipExport { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        SettingsLoaded = Task.Run(Settings.Load);
        _ = Task.Run(Recents.Load);
        _ = Task.Run(Resume.Load);
        _ = Task.Run(Clips.Load);
        Task windowLoaded = Task.Run(WindowState.Load);
        var window = new MainWindow();
        _window = window;
        RestorePlacement(window, windowLoaded);

        string? launchPath = LaunchPath(args);
        lock (RedirectGate)
        {
            s_window = window;
            s_queue = window.DispatcherQueue;
            if (s_earlyActivation)
            {
                launchPath = s_earlyPath ?? launchPath;
                s_earlyPath = null;
                s_earlyActivation = false;
            }
        }
        if (launchPath is { } path)
        {
            window.OpenWhenReady(path);
        }
        window.Activate();
    }

    // Both files are a few bytes and normally read by now; if not, the default size beats a slow start.
    private static void RestorePlacement(MainWindow window, Task windowLoaded)
    {
        if (Task.WaitAll([SettingsLoaded, windowLoaded], RestoreWait)
            && Settings.Current.RememberWindowSize
            && WindowState.Saved is { } saved)
        {
            window.RestorePlacement(saved);
        }
    }

    /// <summary>
    /// Any thread: another launch was handed to this window (Open in the running window). Opens its file, if it
    /// has one, and brings the window to the front.
    /// </summary>
    public static void OnRedirectedActivation(AppActivationArguments arguments)
    {
        string? path = LaunchPath(arguments);
        lock (RedirectGate)
        {
            if (s_window is not { } window || s_queue is not { } queue)
            {
                s_earlyPath = path;
                s_earlyActivation = true;
                return;
            }
            queue.TryEnqueue(() =>
            {
                if (path is not null)
                {
                    window.OpenWhenReady(path);
                }
                window.BringToFront();
            });
        }
    }

    private static string? LaunchPath(AppActivationArguments arguments)
    {
        try
        {
            return arguments.Data switch
            {
                Windows.ApplicationModel.Activation.IFileActivatedEventArgs files =>
                    LaunchArguments.FirstMediaPath(files.Files.Select(f => f.Path)),
                Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch =>
                    LaunchArguments.FirstMediaPath(LaunchArguments.Split(launch.Arguments)),
                _ => null,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The media file the app was started with: a file activation (double-click) or a command-line argument.</summary>
    private static string? LaunchPath(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            if (Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs() is
                { Kind: Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File, Data: Windows.ApplicationModel.Activation.IFileActivatedEventArgs files })
            {
                return LaunchArguments.FirstMediaPath(files.Files.Select(f => f.Path));
            }
        }
        catch (Exception)
        {
            // Fall back to the command line below.
        }
        return LaunchArguments.FirstMediaPath(LaunchArguments.Split(args.Arguments));
    }
}
