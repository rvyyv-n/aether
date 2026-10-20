using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class Ole32
{
    public const uint CLSCTX_INPROC_SERVER = 1;
    public const uint CLSCTX_ALL = 0x17;
    public const uint COINIT_MULTITHREADED = 0;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint reserved, uint coInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();
}
