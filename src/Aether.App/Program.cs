using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

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
        // Unpackaged WinUI 3 MRT Core and native dependency resolution require the base directory
        // to be the working directory when launched via shell verbs, shortcuts, or external paths.
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);

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
        return 0;
    }
}
