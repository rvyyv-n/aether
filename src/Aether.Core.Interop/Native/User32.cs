using System.Runtime.InteropServices;

namespace Aether.Core.Interop.Native;

internal static partial class User32
{
    /// <summary>Adjusts the calling thread's cursor display counter; returns the new count (shown while >= 0).</summary>
    [LibraryImport("user32.dll")]
    public static partial int ShowCursor(int show);
}
