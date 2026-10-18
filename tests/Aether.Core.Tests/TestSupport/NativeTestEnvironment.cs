using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Aether.Core.Interop;
using Aether.Core.Interop.Mpv;
using Xunit;

namespace Aether.Core.Tests.TestSupport;

public static class NativeTestEnvironment
{
    private static readonly Lazy<bool> s_initialized = new(Initialize);

    public static bool EnsureInitialized() => s_initialized.Value;

    private static bool Initialize()
    {
        if (NativeLibraries.SearchDirectory is null)
        {
            string arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64",
            };

            string[] candidateDirs =
            [
                AppContext.BaseDirectory,
                RepoPath.Get("src", "Aether.App", "Native", arch),
                RepoPath.Get("src", "Aether.App", "bin", arch, "Debug", "net10.0-windows10.0.26100.0", $"win-{arch}"),
                RepoPath.Get("src", "Aether.App", "bin", arch, "Release", "net10.0-windows10.0.26100.0", $"win-{arch}"),
                RepoPath.Get("dist", "Aether-portable"),
            ];

            foreach (string candidate in candidateDirs)
            {
                if (Directory.Exists(candidate) &&
                    (File.Exists(Path.Combine(candidate, "libmpv-2.dll")) || File.Exists(Path.Combine(candidate, "mpv-2.dll"))))
                {
                    NativeLibraries.SearchDirectory = candidate;
                    break;
                }
            }
        }

        return true;
    }

    public static bool TryFindBinary(string binaryName, out string? fullPath)
    {
        EnsureInitialized();

        string arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64",
        };

        string[] candidateDirs =
        [
            NativeLibraries.SearchDirectory ?? AppContext.BaseDirectory,
            AppContext.BaseDirectory,
            RepoPath.Get("src", "Aether.App", "Native", arch),
            RepoPath.Get("src", "Aether.App", "bin", arch, "Debug", "net10.0-windows10.0.26100.0", $"win-{arch}"),
            RepoPath.Get("src", "Aether.App", "bin", arch, "Release", "net10.0-windows10.0.26100.0", $"win-{arch}"),
            RepoPath.Get("dist", "Aether-portable"),
        ];

        foreach (string dir in candidateDirs)
        {
            if (Directory.Exists(dir))
            {
                string path = Path.Combine(dir, binaryName);
                if (File.Exists(path))
                {
                    fullPath = path;
                    return true;
                }
            }
        }

        fullPath = null;
        return false;
    }

    public static bool IsMpvAvailable(out string? skipReason)
    {
        EnsureInitialized();
        if (!NativeLibraries.IsMpvAvailable())
        {
            skipReason = "libmpv binary (libmpv-2.dll or mpv-2.dll) was not found or could not be loaded; test skipped.";
            return false;
        }

        skipReason = null;
        return true;
    }

    public static bool IsAngleAvailable(out string? skipReason)
    {
        EnsureInitialized();
        if (!NativeLibraries.IsAngleAvailable())
        {
            skipReason = "ANGLE binary (libEGL.dll) was not found or could not be loaded; test skipped.";
            return false;
        }

        skipReason = null;
        return true;
    }

    public static bool IsFfmpegAvailable(out string? ffmpegPath)
    {
        string binaryName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
        string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string entry in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(entry, binaryName);
            if (File.Exists(candidate))
            {
                ffmpegPath = candidate;
                return true;
            }
        }

        ffmpegPath = null;
        return false;
    }

    public static string? GetBinaryVersion(string binaryPath)
    {
        try
        {
            if (!File.Exists(binaryPath))
            {
                return null;
            }
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(binaryPath);
            string? version = info.ProductVersion ?? info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch
        {
            return null;
        }
    }

    public static void GenerateTinyTestClip(string ffmpegPath, string outputPath, int durationSeconds = 2)
    {
        var psi = new ProcessStartInfo(ffmpegPath)
        {
            ArgumentList =
            {
                "-v", "error",
                "-f", "lavfi",
                "-i", $"testsrc=duration={durationSeconds}:size=160x120:rate=30",
                "-c:v", "mpeg4",
                "-g", "15",
                "-y",
                outputPath,
            },
            CreateNoWindow = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch ffmpeg process.");
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg failed with exit code {process.ExitCode}.");
        }
    }
}

public sealed class NativeBinariesFactAttribute : FactAttribute
{
    public NativeBinariesFactAttribute()
    {
        if (!NativeTestEnvironment.IsMpvAvailable(out string? mpvReason))
        {
            Skip = mpvReason;
        }
        else if (!NativeTestEnvironment.IsAngleAvailable(out string? angleReason))
        {
            Skip = angleReason;
        }
    }
}

public sealed class MpvFactAttribute : FactAttribute
{
    public MpvFactAttribute()
    {
        if (!NativeTestEnvironment.IsMpvAvailable(out string? skipReason))
        {
            Skip = skipReason;
        }
    }
}
