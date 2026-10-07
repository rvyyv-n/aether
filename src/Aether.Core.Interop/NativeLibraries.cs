using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace Aether.Core.Interop;

/// <summary>
/// Resolves the native libraries this assembly imports. libmpv ships as <c>mpv-2.dll</c> or
/// <c>libmpv-2.dll</c> depending on the build; ANGLE as <c>libEGL.dll</c> + <c>libGLESv2.dll</c>.
/// Set <see cref="SearchDirectory"/> before the first native call to load them from a fixed folder.
/// </summary>
public static class NativeLibraries
{
    internal const string LibMpv = "mpv-2.dll";
    internal const string LibEgl = "libEGL.dll";

    private static int _registered;

    /// <summary>Folder probed before the default search path, or null.</summary>
    public static string? SearchDirectory { get; set; }

    internal static void EnsureResolver()
    {
        if (Interlocked.Exchange(ref _registered, 1) == 0)
        {
            NativeLibrary.SetDllImportResolver(typeof(NativeLibraries).Assembly, Resolve);
        }
    }

    /// <summary>True if libmpv can be loaded. Does not keep the module loaded beyond the process default.</summary>
    public static bool IsMpvAvailable() => TryLoad(LibMpv, out _);

    /// <summary>True if ANGLE's EGL library can be loaded.</summary>
    public static bool IsAngleAvailable() => TryLoad(LibEgl, out _);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        TryLoad(libraryName, out nint handle) ? handle : 0;

    private static bool TryLoad(string libraryName, out nint handle)
    {
        ReadOnlySpan<string> candidates = libraryName switch
        {
            LibMpv => [LibMpv, "libmpv-2.dll"],
            _ => [libraryName],
        };

        string? dir = SearchDirectory;
        foreach (string name in candidates)
        {
            if (dir is not null && NativeLibrary.TryLoad(Path.Combine(dir, name), out handle))
            {
                return true;
            }
            if (NativeLibrary.TryLoad(name, typeof(NativeLibraries).Assembly, DllImportSearchPath.SafeDirectories, out handle))
            {
                return true;
            }
        }
        handle = 0;
        return false;
    }
}
