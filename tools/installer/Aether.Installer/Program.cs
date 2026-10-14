using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Aether.Installer;

internal static partial class Program
{
    private const string AppName = "Aether";
    private const string AppVersion = "1.0.0";
    private const string AppPublisher = "Aether Studio";
    private const string MainExeName = "Aether.App.exe";
    private const string InstallerExeName = "AetherInstaller.exe";

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

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
            Console.WriteLine($"           {AppName.ToUpperInvariant()} - STANDALONE PER-USER INSTALLER v{AppVersion}");
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

        try
        {
            // Close any existing instances running from targetDir before overwriting
            KillRunningInstances(targetDir);

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
            RegisterUninstall(targetDir);
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

    private static void DeployPayload(string targetDir)
    {
        Assembly asm = Assembly.GetExecutingAssembly();
        string[] names = asm.GetManifestResourceNames();
        string? resourceName = names.FirstOrDefault(n => n.Equals("payload.zip", StringComparison.OrdinalIgnoreCase) ||
                                                         n.EndsWith(".payload.zip", StringComparison.OrdinalIgnoreCase) ||
                                                         n.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
        using Stream? resourceStream = resourceName != null ? asm.GetManifestResourceStream(resourceName) : null;

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
        if (string.Equals(Path.GetFullPath(sourceDir).TrimEnd('\\'), Path.GetFullPath(targetDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
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

    private static void KillRunningInstances(string targetDir)
    {
        try
        {
            foreach (Process proc in Process.GetProcessesByName("Aether.App"))
            {
                try
                {
                    string? mainModule = proc.MainModule?.FileName;
                    if (mainModule != null && mainModule.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        proc.WaitForExit(3000);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private static bool IsAetherInstall(string dir) => File.Exists(Path.Combine(dir, MainExeName));

    private static string? RegisteredInstallLocation()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
        return key?.GetValue("InstallLocation") is string location && location.Length > 0 ? location : null;
    }

    private static void RegisterUninstall(string targetDir)
    {
        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppName);
        if (key == null) return;

        string mainExe = Path.Combine(targetDir, MainExeName);
        string installerExe = Path.Combine(targetDir, InstallerExeName);

        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", AppVersion);
        key.SetValue("Publisher", AppPublisher);
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
            string progId = "Aether.VideoPlayer";
            using RegistryKey? progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{progId}");
            if (progKey != null)
            {
                progKey.SetValue("", "Aether Video File");
                using RegistryKey? defIcon = progKey.CreateSubKey("DefaultIcon");
                defIcon?.SetValue("", $"\"{mainExe}\",0");
                using RegistryKey? openCmd = progKey.CreateSubKey(@"shell\open\command");
                openCmd?.SetValue("", $"\"{mainExe}\" \"%1\"");
            }
        }
        catch { }
    }

    private static void UnregisterFileAssociations()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\SystemFileAssociations\video\shell\Aether", false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Aether.VideoPlayer", false);
        }
        catch { }
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
