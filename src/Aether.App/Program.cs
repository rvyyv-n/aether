using System.Runtime;
using Aether.Core.Interop.Mpv;
using Aether_App.Updates;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Aether_App;

/// <summary>
/// The entry point, written out (DISABLE_XAML_GENERATED_MAIN) so a launch can be handed to a running Aether
/// before any window is created.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main()
    {
        // Velopack's install, update and uninstall hooks run Aether with special arguments and exit, so this comes before anything else.
        VelopackApp.Build()
            .OnAfterInstallFastCallback(_ => ShellIntegration.RegisterForInstall())
            .OnAfterUpdateFastCallback(_ => ShellIntegration.Register(AppContext.BaseDirectory))
            .OnBeforeUninstallFastCallback(_ => ShellIntegration.RemoveForUninstall())
            .Run();

        // Unpackaged WinUI 3 MRT Core and native dependency resolution require the base directory
        // to be the working directory when launched via shell verbs, shortcuts, or external paths.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        PerfLog.Mark("main_ms");

        // Background collections with the low-pause mode: a full blocking collection mid-playback would drop frames
        // (ADR-0003). The mode needs concurrent collection, which the project file keeps on.
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        PerfLog.WriteGcSettings();

        WinRT.ComWrappersSupport.InitializeComWrappers();
        if (SingleInstance.TryHandOff())
        {
            return 0;
        }
        Application.Start(callback =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
        // Closing the window disposed the engine, whose teardown runs in the background; give it a moment to finish
        // so the process does not end with mpv's threads half stopped, but never hold up exit for long.
        MpvContext.WaitForDestroyedCores(2000);
        MainPage.WaitForLanServerStop(1000);
        return 0;
    }
}
