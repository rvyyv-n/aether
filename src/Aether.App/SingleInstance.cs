using System.Runtime.InteropServices;
using Aether.Core.Playback.Settings;
using Microsoft.Windows.AppLifecycle;

namespace Aether_App;

/// <summary>
/// Hands a launch to the Aether that is already running (Open in the running window). Runs from
/// <see cref="Program.Main"/>, before the app and its window exist.
/// </summary>
internal static partial class SingleInstance
{
    private const string MainKey = "main";
    private const uint RedirectTimeoutMs = 5000;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateEventW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CreateEvent(nint attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string? name);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetEvent(nint handle);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);

    [LibraryImport("ole32.dll")]
    private static unsafe partial int CoWaitForMultipleObjects(uint flags, uint timeout, uint handleCount, nint* handles, out uint index);

    /// <summary>
    /// True when this launch was passed to a running Aether, which the caller should then exit without starting.
    /// Otherwise this process is the one other launches find (unless the setting is off, in which case it
    /// runs alone). Registering is cheap, but reading the setting is not needed when nothing else is running.
    /// </summary>
    public static bool TryHandOff()
    {
        try
        {
            bool running = AppInstance.GetInstances().Any(instance => instance.Key == MainKey);
            if (running && !ReadOpenInRunningWindow())
            {
                return false;
            }

            AppInstance main = AppInstance.FindOrRegisterForKey(MainKey);
            if (main.IsCurrent)
            {
                main.Activated += (_, arguments) => App.OnRedirectedActivation(arguments);
                return false;
            }
            return Redirect(main);
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return false; // no single instance support: just run
        }
    }

    // The settings store loads in the background once the app is up; here nothing else is running yet, and the file is tiny.
    private static bool ReadOpenInRunningWindow()
    {
        var settings = new SettingsStore(new FileSettingsStorage(FileSettingsStorage.DefaultPath()));
        settings.Load();
        return settings.Current.OpenInRunningWindow;
    }

    // RedirectActivationToAsync must not be awaited from a single-threaded apartment thread, because the
    // activation marshals back to it. So the call runs on the pool and this thread pumps COM messages until it ends.
    private static unsafe bool Redirect(AppInstance main)
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        ForegroundWindow.Allow(main.ProcessId);

        nint done = CreateEvent(0, true, false, null);
        if (done == 0)
        {
            return false;
        }
        bool redirected = false;
        bool finished = false;
        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    main.RedirectActivationToAsync(activation).AsTask().Wait();
                    redirected = true;
                }
                catch (Exception)
                {
                    // The running instance went away or hung: this launch opens its own window.
                }
                finally
                {
                    SetEvent(done);
                }
            });
            nint handle = done;
            finished = CoWaitForMultipleObjects(0, RedirectTimeoutMs, 1, &handle, out _) == 0;
            return Volatile.Read(ref redirected);
        }
        finally
        {
            // A timed-out redirect may still signal later, so the handle is only closed once it is done.
            if (finished)
            {
                CloseHandle(done);
            }
        }
    }
}
