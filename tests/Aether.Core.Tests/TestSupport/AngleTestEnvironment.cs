using System;
using System.IO;
using Aether.Core.Interop;

namespace Aether.Core.Tests.TestSupport;

public static class AngleTestEnvironment
{
    private static readonly Lazy<bool> s_isAvailable = new(InitializeAndCheck);

    public static bool IsAvailable => s_isAvailable.Value;

    private static bool InitializeAndCheck()
    {
        if (NativeLibraries.SearchDirectory is null)
        {
            string systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string webViewDir = Path.Combine(systemFolder, "Microsoft-Edge-WebView");
            if (File.Exists(Path.Combine(webViewDir, "libEGL.dll")))
            {
                NativeLibraries.SearchDirectory = webViewDir;
            }
        }

        return NativeLibraries.IsAngleAvailable();
    }
}
