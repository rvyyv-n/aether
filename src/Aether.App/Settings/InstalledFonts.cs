using System.Runtime.InteropServices;

namespace Aether_App.Settings;

/// <summary>The font families installed on this PC (for everyone and for the current user), read through DirectWrite, the same names mpv matches.</summary>
internal static unsafe partial class InstalledFonts
{
    [LibraryImport("dwrite.dll")]
    private static partial int DWriteCreateFactory(int factoryType, in Guid iid, out nint factory);

    private static readonly Guid FactoryIid = new("b859ee5a-d838-4b5b-a2e8-1adc7d93db48");

    // The DirectWrite objects are called through their vtables because Native AOT cannot marshal [ComImport] interfaces.
    // Slot numbers count IUnknown's three methods first, then the interface's own in declaration order.
    private static void** Vtbl(nint p) => *(void***)p;

    private static void Release(nint p) => ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(p)[2])(p);

    /// <summary>Family names in English where the font has one, otherwise in its first language. Empty if DirectWrite is unavailable.</summary>
    public static List<string> Families()
    {
        List<string> names = [];
        nint factory = 0;
        nint collection = 0;
        try
        {
            if (DWriteCreateFactory(0, in FactoryIid, out factory) < 0
                || ((delegate* unmanaged[Stdcall]<nint, nint*, int, int>)Vtbl(factory)[3])(factory, &collection, 0) < 0)
            {
                return names;
            }

            uint count = ((delegate* unmanaged[Stdcall]<nint, uint>)Vtbl(collection)[3])(collection);
            for (uint i = 0; i < count; i++)
            {
                string? name = FamilyName(collection, i);
                if (name is not null)
                {
                    names.Add(name);
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            // The picker falls back to its built-in list.
        }
        finally
        {
            if (collection != 0)
            {
                Release(collection);
            }
            if (factory != 0)
            {
                Release(factory);
            }
        }
        return names;
    }

    private static string? FamilyName(nint collection, uint familyIndex)
    {
        nint family = 0;
        nint strings = 0;
        try
        {
            if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Vtbl(collection)[4])(collection, familyIndex, &family) < 0
                || ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Vtbl(family)[6])(family, &strings) < 0)
            {
                return null;
            }

            uint index;
            int exists;
            fixed (char* locale = "en-us")
            {
                if (((delegate* unmanaged[Stdcall]<nint, char*, uint*, int*, int>)Vtbl(strings)[4])(strings, locale, &index, &exists) < 0 || exists == 0)
                {
                    index = 0;
                }
            }

            uint length;
            if (((delegate* unmanaged[Stdcall]<nint, uint, uint*, int>)Vtbl(strings)[7])(strings, index, &length) < 0)
            {
                return null;
            }

            char[] buffer = new char[length + 1];
            fixed (char* text = buffer)
            {
                return ((delegate* unmanaged[Stdcall]<nint, uint, char*, uint, int>)Vtbl(strings)[8])(strings, index, text, length + 1) >= 0
                    ? new string(text)
                    : null;
            }
        }
        finally
        {
            if (strings != 0)
            {
                Release(strings);
            }
            if (family != 0)
            {
                Release(family);
            }
        }
    }
}
