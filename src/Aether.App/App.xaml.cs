using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Resume;
using Aether.Core.Playback.Settings;
using Aether.PvP.Review.Clipping;
using Aether_App.Input;
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
        var window = new MainWindow();
        _window = window;
        if (LaunchPath(args) is { } path)
        {
            window.OpenWhenReady(path);
        }
        window.Activate();
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
