using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Aether.Core.Playback;
using Microsoft.Win32;

namespace Aether.Installer;

internal static partial class Program
{
    private const string AppName = "Aether";
    private const string MainExeName = "Aether.App.exe";
    private const string InstallerExeName = "AetherInstaller.exe";
    private const string ProgId = "Aether.VideoPlayer";
    private const string ApplicationsKeyPath = @"Software\Classes\Applications\" + MainExeName;
    private const string CapabilitiesKeyPath = @"Software\Aether\Capabilities";
    private const string RegisteredApplicationsKeyPath = @"Software\RegisteredApplications";

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("shell32.dll")]
    private static partial void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [STAThread]
    private static int Main(string[] args)
    {
        // Try attaching to the parent console if invoked from CLI
        AttachConsole(-1);

        bool silent = false;
        bool uninstall = false;
        bool portable = false;
        bool createDesktop = true;
        bool launchAfter = true;
        string? customTarget = null;
        var forwardArgs = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-s", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("-q", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase))
            {
                silent = true;
            }
            else if (arg.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-u", StringComparison.OrdinalIgnoreCase))
            {
                uninstall = true;
            }
            else if (arg.Equals("--portable", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-p", StringComparison.OrdinalIgnoreCase))
            {
                portable = true;
            }
            else if (arg.Equals("--no-desktop-shortcut", StringComparison.OrdinalIgnoreCase))
            {
                createDesktop = false;
            }
            else if (arg.Equals("--no-launch", StringComparison.OrdinalIgnoreCase))
            {
                launchAfter = false;
            }
            else if (arg.Equals("--target", StringComparison.OrdinalIgnoreCase) ||
                     arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    customTarget = args[++i];
                }
            }
            else
            {
                forwardArgs.Add(arg);
            }
        }

        string defaultTarget = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            AppName);

        if (portable)
        {
            return RunPortable(forwardArgs);
        }

        if (uninstall)
        {
            // Settings runs the uninstaller without --target, so a custom install is found through its registry entry.
            string uninstallDir = !string.IsNullOrWhiteSpace(customTarget) ? Path.GetFullPath(customTarget)
                : RegisteredInstallLocation() ?? defaultTarget;
            return ExecuteUninstall(uninstallDir, silent);
        }

        string targetDir = string.IsNullOrWhiteSpace(customTarget) ? defaultTarget : Path.GetFullPath(customTarget);
        return ExecuteInstall(targetDir, createDesktop, launchAfter, silent, forwardArgs);
    }

    private static int RunPortable(List<string> forwardArgs)
    {
        string currentDir = AppContext.BaseDirectory;
        string exePath = Path.Combine(currentDir, MainExeName);
        if (!File.Exists(exePath))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Portable runner could not find {MainExeName} in {currentDir}");
            Console.ResetColor();
            return 1;
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = currentDir,
            UseShellExecute = true
        };
        foreach (string a in forwardArgs)
        {
            startInfo.ArgumentList.Add(a);
        }
        Process.Start(startInfo);
        return 0;
    }

    private static int ExecuteInstall(
        string targetDir,
        bool createDesktop,
        bool launchAfter,
        bool silent,
        List<string> forwardArgs)
    {
        if (!silent)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("====================================================================");
            Console.WriteLine($"           {AppName.ToUpperInvariant()} - STANDALONE PER-USER INSTALLER");
            Console.WriteLine("      Zero Administrator Privileges Required | %LOCALAPPDATA%       ");
            Console.WriteLine("====================================================================");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"Target Installation Directory:");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($"  {targetDir}");
            Console.ResetColor();
            Console.WriteLine();
        }

        // Uninstall deletes the whole folder, so never install into one that holds anything else.
        if (Directory.Exists(targetDir) && Directory.EnumerateFileSystemEntries(targetDir).Any() && !IsAetherInstall(targetDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] {targetDir} already has other files in it. Choose an empty folder.");
            Console.ResetColor();
            return 1;
        }

        // Check the payload first so an upgrade never wipes the old install with nothing to put in its place.
        if (!PayloadAvailable())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Could not find the {AppName} files to install.");
            Console.ResetColor();
            return 1;
        }

        if (!ConfirmCloseRunning(targetDir, silent))
        {
            return 1;
        }

        try
        {
            KillRunningInstances(targetDir);

            if (IsAetherInstall(targetDir) && !InstallingInPlace(targetDir))
            {
                RemoveOldFiles(targetDir);
            }

            // Step 1: Deploy application files
            if (!silent)
            {
                Console.Write("[1/5] Deploying application files... ");
            }

            Directory.CreateDirectory(targetDir);
            DeployPayload(targetDir);

            // Copy self as uninstaller into the target directory
            string selfPath = Environment.ProcessPath ?? "";
            if (File.Exists(selfPath))
            {
                string targetInstaller = Path.Combine(targetDir, InstallerExeName);
                if (!string.Equals(selfPath, targetInstaller, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(selfPath, targetInstaller, true);
                }
            }

            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Done.");
                Console.ResetColor();
            }

            string mainExePath = Path.Combine(targetDir, MainExeName);
            (string displayVersion, string publisher) = ReadAppInfo(mainExePath);
            string iconPath = Path.Combine(targetDir, "Assets", "AppIcon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = mainExePath;
            }

            // Step 2: Create Start Menu shortcut
            if (!silent)
            {
                Console.Write("[2/5] Creating Start Menu shortcut... ");
            }
            string startMenuDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs");
            Directory.CreateDirectory(startMenuDir);
            string startMenuShortcut = Path.Combine(startMenuDir, $"{AppName}.lnk");
            CreateShortcut(startMenuShortcut, mainExePath, targetDir, iconPath, $"{AppName} Video Player");
            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Done.");
                Console.ResetColor();
            }

            // Step 3: Create Desktop shortcut
            if (createDesktop)
            {
                if (!silent)
                {
                    Console.Write("[3/5] Creating Desktop shortcut... ");
                }
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string desktopShortcut = Path.Combine(desktopDir, $"{AppName}.lnk");
                CreateShortcut(desktopShortcut, mainExePath, targetDir, iconPath, $"{AppName} Video Player");
                if (!silent)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Done.");
                    Console.ResetColor();
                }
            }
            else if (!silent)
            {
                Console.WriteLine("[3/5] Skipping Desktop shortcut.");
            }

            // Step 4: Register in Windows HKCU (Add/Remove Programs & App Paths)
            if (!silent)
            {
                Console.Write("[4/5] Registering in Windows (Installed Apps & Run)... ");
            }
            RegisterUninstall(targetDir, displayVersion, publisher);
            RegisterAppPaths(targetDir);
            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Done.");
                Console.ResetColor();
            }

            // Step 5: Register Context Menu & File Associations in HKCU
            if (!silent)
            {
                Console.Write("[5/5] Registering shell context menus ('Play with Aether')... ");
            }
            RegisterFileAssociations(mainExePath);
            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("Done.");
                Console.ResetColor();
            }

            if (!silent)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] {AppName} has been installed successfully without requiring Admin rights!");
                Console.ResetColor();
            }

            // Launch app if requested
            if (launchAfter)
            {
                if (!silent)
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Launching {AppName}...");
                    Console.ResetColor();
                }

                var startInfo = new ProcessStartInfo(mainExePath)
                {
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                };
                foreach (string a in forwardArgs)
                {
                    startInfo.ArgumentList.Add(a);
                }
                Process.Start(startInfo);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Error] Installation failed: {ex.Message}");
            Console.WriteLine(ex);
            Console.ResetColor();
            return 1;
        }
    }

    private static int ExecuteUninstall(string targetDir, bool silent)
    {
        if (!silent)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Uninstalling {AppName} from {targetDir}...");
            Console.ResetColor();
        }

        if (Directory.Exists(targetDir) && !IsAetherInstall(targetDir))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] {targetDir} is not an {AppName} install, so nothing was removed.");
            Console.ResetColor();
            return 1;
        }

        if (!ConfirmCloseRunning(targetDir, silent))
        {
            return 1;
        }

        try
        {
            KillRunningInstances(targetDir);

            // Remove Start Menu shortcut
            string startMenuShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Start Menu\Programs",
                $"{AppName}.lnk");
            if (File.Exists(startMenuShortcut))
            {
                File.Delete(startMenuShortcut);
            }

            // Remove Desktop shortcut
            string desktopShortcut = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{AppName}.lnk");
            if (File.Exists(desktopShortcut))
            {
                File.Delete(desktopShortcut);
            }

            // Remove HKCU Registry keys
            UnregisterUninstall();
            UnregisterAppPaths();
            UnregisterFileAssociations();

            // Self-delete or clean target directory
            string selfPath = Environment.ProcessPath ?? "";
            bool isSelfInTarget = !string.IsNullOrEmpty(selfPath) &&
                                  selfPath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase);

            if (isSelfInTarget)
            {
                // Delete other files now and schedule directory removal via cmd.exe
                foreach (string file in Directory.EnumerateFiles(targetDir))
                {
                    if (!string.Equals(file, selfPath, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                foreach (string dir in Directory.EnumerateDirectories(targetDir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }

                // Schedule background self-deletion
                Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 1 /nobreak > nul & rd /s /q \"{targetDir}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
            }
            else
            {
                if (Directory.Exists(targetDir))
                {
                    Directory.Delete(targetDir, true);
                }
            }

            if (!silent)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] {AppName} was uninstalled successfully.");
                Console.ResetColor();
            }
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Uninstall failed: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static Stream? OpenEmbeddedPayload()
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string? resourceName = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        return resourceName != null ? asm.GetManifestResourceStream(resourceName) : null;
    }

    private static bool PayloadAvailable()
    {
        using Stream? resourceStream = OpenEmbeddedPayload();
        return resourceStream != null || File.Exists(Path.Combine(AppContext.BaseDirectory, MainExeName));
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    // Running the installer from the install folder itself with no embedded payload: the files to deploy are the ones already there.
    private static bool InstallingInPlace(string targetDir)
    {
        using Stream? resourceStream = OpenEmbeddedPayload();
        return resourceStream == null && SamePath(AppContext.BaseDirectory, targetDir);
    }

    private static void RemoveOldFiles(string targetDir)
    {
        string selfPath = Environment.ProcessPath ?? "";
        foreach (string file in Directory.EnumerateFiles(targetDir))
        {
            if (selfPath.Length == 0 || !SamePath(file, selfPath))
            {
                File.Delete(file);
            }
        }
        foreach (string dir in Directory.EnumerateDirectories(targetDir))
        {
            Directory.Delete(dir, true);
        }
    }

    private static void DeployPayload(string targetDir)
    {
        using Stream? resourceStream = OpenEmbeddedPayload();

        if (resourceStream != null)
        {
            // Embedded zip payload mode
            using var archive = new ZipArchive(resourceStream, ZipArchiveMode.Read);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
                {
                    Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                    continue;
                }

                string destinationPath = Path.Combine(targetDir, entry.FullName);
                string? destDir = Path.GetDirectoryName(destinationPath);
                if (destDir != null)
                {
                    Directory.CreateDirectory(destDir);
                }
                entry.ExtractToFile(destinationPath, true);
            }
            return;
        }

        // Running beside unpacked files mode (e.g. from portable build folder)
        string sourceDir = AppContext.BaseDirectory;
        if (SamePath(sourceDir, targetDir))
        {
            return; // Already in target directory
        }

        CopyDirectory(sourceDir, targetDir);
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            string fileName = Path.GetFileName(file);
            if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string dest = Path.Combine(targetDir, fileName);
            File.Copy(file, dest, true);
        }
        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            string subDirName = Path.GetFileName(subDir);
            if (subDirName.Equals("installer-build", StringComparison.OrdinalIgnoreCase) ||
                subDirName.Equals("Aether-portable", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            CopyDirectory(subDir, Path.Combine(targetDir, subDirName));
        }
    }

    private static List<Process> FindRunningInstances(string targetDir)
    {
        var running = new List<Process>();
        string prefix = Path.TrimEndingDirectorySeparator(targetDir) + Path.DirectorySeparatorChar;
        try
        {
            foreach (Process proc in Process.GetProcessesByName("Aether.App"))
            {
                try
                {
                    string? mainModule = proc.MainModule?.FileName;
                    if (mainModule != null && mainModule.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        running.Add(proc);
                    }
                }
                catch { }
            }
        }
        catch { }
        return running;
    }

    private static void KillRunningInstances(string targetDir)
    {
        foreach (Process proc in FindRunningInstances(targetDir))
        {
            try
            {
                proc.Kill();
                proc.WaitForExit(3000);
            }
            catch { }
        }
    }

    // Only asks when a console can actually answer; with none (launched from Settings) or redirected input it just proceeds.
    private static bool ConfirmCloseRunning(string targetDir, bool silent)
    {
        if (silent || Console.IsInputRedirected || Console.IsOutputRedirected || FindRunningInstances(targetDir).Count == 0)
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"{AppName} is running from {targetDir} and will be closed.");
        Console.ResetColor();
        Console.Write("Continue? [Y/n] ");
        string? answer = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(answer) || answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[Cancelled] {AppName} was left running. Close it yourself and run this again.");
        Console.ResetColor();
        return false;
    }

    private static (string Version, string Publisher) ReadAppInfo(string mainExe)
    {
        FileVersionInfo info = FileVersionInfo.GetVersionInfo(mainExe);
        string version = (info.ProductVersion ?? "").Split('+')[0].Trim();
        if (version.Length == 0)
        {
            version = (info.FileVersion ?? "").Trim();
        }
        if (version.Length == 0)
        {
            version = "0.0.0";
        }

        // The SDK fills CompanyName with the assembly name when none is set, which isn't a real publisher.
        string company = (info.CompanyName ?? "").Trim();
        bool isDefault = company.Length == 0 ||
                         company.Equals(Path.GetFileNameWithoutExtension(mainExe), StringComparison.OrdinalIgnoreCase) ||
                         company.Equals(info.ProductName, StringComparison.OrdinalIgnoreCase);
        return (version, isDefault ? AppName : company);
    }

    private static bool IsAetherInstall(string dir) => File.Exists(Path.Combine(dir, MainExeName));

    private static string? RegisteredInstallLocation()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
        return key?.GetValue("InstallLocation") is string location && location.Length > 0 ? location : null;
    }

    private static void RegisterUninstall(string targetDir, string displayVersion, string publisher)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
        if (key == null) return;

        string mainExe = Path.Combine(targetDir, MainExeName);
        string installerExe = Path.Combine(targetDir, InstallerExeName);

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", displayVersion);
        key.SetValue("Publisher", publisher);
        key.SetValue("DisplayIcon", $"{mainExe},0");
        key.SetValue("InstallLocation", targetDir);
        key.SetValue("UninstallString", $"\"{installerExe}\" --uninstall");
        key.SetValue("QuietUninstallString", $"\"{installerExe}\" --uninstall --silent");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        try
        {
            long totalBytes = Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
            key.SetValue("EstimatedSize", (int)(totalBytes / 1024), RegistryValueKind.DWord);
        }
        catch { }
    }

    private static void UnregisterUninstall()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName, false);
        }
        catch { }
    }

    private static void RegisterAppPaths(string targetDir)
    {
        string mainExe = Path.Combine(targetDir, MainExeName);
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\aether.exe");
        if (key != null)
        {
            key.SetValue("", mainExe);
            key.SetValue("Path", targetDir);
        }
    }

    private static void UnregisterAppPaths()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\App Paths\aether.exe", false);
        }
        catch { }
    }

    private static void RegisterFileAssociations(string mainExe)
    {
        try
        {
            // Context menu for all video files under HKCU\Software\Classes\SystemFileAssociations\video\shell\Aether
            using RegistryKey? shellKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\SystemFileAssociations\video\shell\Aether");
            if (shellKey != null)
            {
                shellKey.SetValue("", "Play with Aether");
                shellKey.SetValue("Icon", $"\"{mainExe}\",0");
                using RegistryKey? cmdKey = shellKey.CreateSubKey("command");
                cmdKey?.SetValue("", $"\"{mainExe}\" \"%1\"");
            }

            // ProgId for direct file associations
            using RegistryKey? progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}");
            if (progKey != null)
            {
                progKey.SetValue("", "Aether Video File");
                using RegistryKey? defIcon = progKey.CreateSubKey("DefaultIcon");
                defIcon?.SetValue("", $"\"{mainExe}\",0");
                using RegistryKey? openCmd = progKey.CreateSubKey(@"shell\open\command");
                openCmd?.SetValue("", $"\"{mainExe}\" \"%1\"");
            }

            // "Open with" list entries for each supported type, without claiming the default app.
            foreach (string ext in MediaFormats.Extensions)
            {
                using RegistryKey? withKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}\OpenWithProgids");
                withKey?.SetValue(ProgId, "");
            }

            using RegistryKey? appKey = Registry.CurrentUser.CreateSubKey(ApplicationsKeyPath);
            if (appKey != null)
            {
                appKey.SetValue("FriendlyAppName", AppName);
                using RegistryKey? appCmd = appKey.CreateSubKey(@"shell\open\command");
                appCmd?.SetValue("", $"\"{mainExe}\" \"%1\"");
                using RegistryKey? types = appKey.CreateSubKey("SupportedTypes");
                foreach (string ext in MediaFormats.Extensions)
                {
                    types?.SetValue(ext, "");
                }
            }

            // Capabilities make Aether show up in Settings > Default apps.
            using RegistryKey? capKey = Registry.CurrentUser.CreateSubKey(CapabilitiesKeyPath);
            if (capKey != null)
            {
                capKey.SetValue("ApplicationName", AppName);
                capKey.SetValue("ApplicationDescription", $"{AppName} Video Player");
                using RegistryKey? assocKey = capKey.CreateSubKey("FileAssociations");
                foreach (string ext in MediaFormats.Extensions)
                {
                    assocKey?.SetValue(ext, ProgId);
                }
            }
            using RegistryKey? registered = Registry.CurrentUser.CreateSubKey(RegisteredApplicationsKeyPath);
            registered?.SetValue(AppName, CapabilitiesKeyPath);
        }
        catch { }

        NotifyAssociationsChanged();
    }

    private static void UnregisterFileAssociations()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\Aether", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{ProgId}", false);
            Registry.CurrentUser.DeleteSubKeyTree(ApplicationsKeyPath, false);
            Registry.CurrentUser.DeleteSubKeyTree(CapabilitiesKeyPath, false);

            using (RegistryKey? vendorKey = Registry.CurrentUser.OpenSubKey(@"Software\Aether"))
            {
                if (vendorKey != null && vendorKey.SubKeyCount == 0 && vendorKey.ValueCount == 0)
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
        catch { }

        NotifyAssociationsChanged();
    }

    private static void NotifyAssociationsChanged()
    {
        const int SHCNE_ASSOCCHANGED = 0x08000000;
        SHChangeNotify(SHCNE_ASSOCCHANGED, 0, IntPtr.Zero, IntPtr.Zero);
    }

    private static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string workingDirectory,
        string iconPath,
        string description)
    {
        IShellLinkW link = (IShellLinkW)new ShellLink();
        link.SetPath(targetPath);
        link.SetWorkingDirectory(workingDirectory);
        link.SetDescription(description);
        if (!string.IsNullOrEmpty(iconPath))
        {
            link.SetIconLocation(iconPath, 0);
        }
        ((IPersistFile)link).Save(shortcutPath, true);
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }
}
