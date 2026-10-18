using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Native;

internal static unsafe partial class Ole32
{
    public const uint CLSCTX_INPROC_SERVER = 1;

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* clsid, nint outer, uint context, Guid* iid, nint* instance);
}
