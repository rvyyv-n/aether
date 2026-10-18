using System.Runtime.InteropServices;
using Aether.Core.Playback;
using Microsoft.Win32;

namespace Aether_App.Updates;

/// <summary>
/// The Windows entries the old AetherInstaller wrote: App Paths, "Play with Aether", Open with and the Default apps entry. Velopack's
/// install and uninstall hooks call this, since its Setup creates the shortcuts and Installed apps entry but nothing else.
/// Everything is per user and best effort: a locked key never stops an install or an update.
/// </summary>
internal static partial class ShellIntegration
{
    private const string AppName = "Aether";
    private const string MainExeName = "Aether.App.exe";
    private const string ProgId = "Aether.VideoPlayer";
    private const string ApplicationsKeyPath = @"Software\Classes\Applications\" + MainExeName;
    private const string CapabilitiesKeyPath = @"Software\Aether\Capabilities";
    private const string RegisteredApplicationsKeyPath = @"Software\RegisteredApplications";
    private const string AppPathsKeyPath = @"Software\Microsoft\Windows\CurrentVersion\App Paths\aether.exe";

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static void RegisterForInstall()
    {
        Register(AppContext.BaseDirectory);
        RemoveLegacyInstall();
    }

    public static void RemoveForUninstall()
    {
        Unregister();
        RemoveDownloadedTools();
    }

    public static void Register(string installDir)
    {
        string mainExe = Path.Combine(installDir, MainExeName);
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(AppPathsKeyPath))
            {
                key.SetValue("", mainExe);
                key.SetValue("Path", installDir);
            }

            using (RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SystemFileAssociations\video\shell\Aether"))
            {
                shellKey.SetValue("", "Play with Aether");
                shellKey.SetValue("Icon", $"\"{mainExe}\",0");
                using RegistryKey cmdKey = shellKey.CreateSubKey("command");
                cmdKey.SetValue("", $"\"{mainExe}\" \"%1\"");
            }

            using (RegistryKey progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progKey.SetValue("", "Aether Video File");
                using RegistryKey defIcon = progKey.CreateSubKey("DefaultIcon");
                defIcon.SetValue("", $"\"{mainExe}\",0");
                using RegistryKey openCmd = progKey.CreateSubKey(@"shell\open\command");
                openCmd.SetValue("", $"\"{mainExe}\" \"%1\"");
            }

            // Listed under Open with without claiming the default app.
            foreach (string ext in MediaFormats.Extensions)
            {
                using RegistryKey withKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                withKey.SetValue(ProgId, "");
            }

            using (RegistryKey appKey = Registry.CurrentUser.CreateSubKey(ApplicationsKeyPath))
            {
                appKey.SetValue("FriendlyAppName", AppName);
                using RegistryKey appCmd = appKey.CreateSubKey(@"shell\open\command");
                appCmd.SetValue("", $"\"{mainExe}\" \"%1\"");
                using RegistryKey types = appKey.CreateSubKey("SupportedTypes");
                foreach (string ext in MediaFormats.Extensions)
                {
                    types.SetValue(ext, "");
                }
            }

            using (RegistryKey capKey = Registry.CurrentUser.CreateSubKey(CapabilitiesKeyPath))
            {
                capKey.SetValue("ApplicationName", AppName);
                capKey.SetValue("ApplicationDescription", $"{AppName} Video Player");
                using RegistryKey assocKey = capKey.CreateSubKey("FileAssociations");
                foreach (string ext in MediaFormats.Extensions)
                {
                    assocKey.SetValue(ext, ProgId);
                }
            }
            using RegistryKey registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKeyPath);
            registered.SetValue(AppName, CapabilitiesKeyPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Aether still runs; only the Open with entries are missing until the next update retries.
        }
        NotifyAssociationsChanged();
    }

    public static void Unregister()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppPathsKeyPath, false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\Aether", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
            Registry.CurrentUser.DeleteSubKeyTree(ApplicationsKeyPath, false);
            Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKeyPath, false);

            using (RegistryKey? vendorKey = Registry.CurrentUser.OpenSubKey(@"Software\Aether"))
            {
                if (vendorKey is { SubKeyCount: 0, ValueCount: 0 })
                {
                    vendorKey.Dispose();
                    Registry.CurrentUser.DeleteSubKey(@"Software\Aether", false);
                }
            }
            using (RegistryKey? registered = Registry.CurrentUser.OpenSubKey(RegisteredApplicationsKeyPath, true))
            {
                registered?.DeleteValue(AppName, false);
            }
            foreach (string ext in MediaFormats.Extensions)
            {
                using RegistryKey? withKey = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{ext}\OpenWithProgids", true);
                withKey?.DeleteValue(ProgId, false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Leftover keys point at a missing program and do no harm.
        }
        NotifyAssociationsChanged();
    }

    /// <summary>Removes the downloaded yt-dlp copy, which lives outside the install folder; user settings stay.</summary>
    public static void RemoveDownloadedTools()
    {
        try
        {
            if (Directory.Exists(Settings.YtDlpManager.ToolsFolder))
            {
                Directory.Delete(Settings.YtDlpManager.ToolsFolder, true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left behind; it is a few megabytes in the user's own profile.
        }
    }

    /// <summary>
    /// Removes the home-made installer's copy in <c>%LOCALAPPDATA%\Programs\Aether</c> and its Installed apps entry, once Velopack's copy
    /// has taken over. Only the folder and that entry go: the shortcuts share a name with Velopack's and the shell entries were just
    /// rewritten to the new path, so the old uninstaller must not run.
    /// </summary>
    public static void RemoveLegacyInstall()
    {
        try
        {
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);
            bool isOurs = File.Exists(Path.Combine(legacy, MainExeName)) && File.Exists(Path.Combine(legacy, "AetherInstaller.exe"));
            if (!isOurs)
            {
                return;
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);
            Directory.Delete(legacy, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The old copy was still running; it is harmless and goes the next time this runs.
        }
    }

    private static void NotifyAssociationsChanged()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }
}
