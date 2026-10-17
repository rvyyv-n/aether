using System.Runtime.InteropServices;
using System.Text;

namespace Aether_App.Settings;

/// <summary>The font families installed on this PC (for everyone and for the current user), read through DirectWrite, the same names mpv matches.</summary>
internal static class InstalledFonts
{
    [DllImport("dwrite.dll")]
    private static extern int DWriteCreateFactory(int factoryType, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    // Only the methods up to the ones used are declared, but every slot before them must be present, in order.
    [ComImport, Guid("b859ee5a-d838-4b5b-a2e8-1adc7d93db48"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFactory
    {
        [PreserveSig] int GetSystemFontCollection(out IDWriteFontCollection collection, [MarshalAs(UnmanagedType.Bool)] bool checkForUpdates);
    }

    [ComImport, Guid("a84cee02-3eea-4eee-a827-87c1a02a0fcc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontCollection
    {
        [PreserveSig] uint GetFontFamilyCount();
        [PreserveSig] int GetFontFamily(uint index, out IDWriteFontFamily family);
    }

    [ComImport, Guid("da20d8ef-812a-4c43-9802-62ec4abd7add"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteFontFamily
    {
        // IDWriteFontList slots.
        [PreserveSig] int GetFontCollection(out IDWriteFontCollection collection);
        [PreserveSig] uint GetFontCount();
        [PreserveSig] int GetFont(uint index, [MarshalAs(UnmanagedType.IUnknown)] out object font);
        [PreserveSig] int GetFamilyNames(out IDWriteLocalizedStrings names);
    }

    [ComImport, Guid("08256209-099a-4b34-b86d-c22b110e7771"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDWriteLocalizedStrings
    {
        [PreserveSig] uint GetCount();
        [PreserveSig] int FindLocaleName([MarshalAs(UnmanagedType.LPWStr)] string localeName, out uint index, [MarshalAs(UnmanagedType.Bool)] out bool exists);
        [PreserveSig] int GetStringLength(uint index, out uint length);
        [PreserveSig] int GetString(uint index, [MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer, uint size);
    }

    /// <summary>Family names in English where the font has one, otherwise in its first language. Empty if DirectWrite is unavailable.</summary>
    public static List<string> Families()
    {
        List<string> names = [];
        try
        {
            Guid factoryIid = typeof(IDWriteFactory).GUID;
            if (DWriteCreateFactory(0, ref factoryIid, out object created) < 0
                || ((IDWriteFactory)created).GetSystemFontCollection(out IDWriteFontCollection collection, false) < 0)
            {
                return names;
            }

            uint count = collection.GetFontFamilyCount();
            for (uint i = 0; i < count; i++)
            {
                if (collection.GetFontFamily(i, out IDWriteFontFamily family) < 0
                    || family.GetFamilyNames(out IDWriteLocalizedStrings strings) < 0)
                {
                    continue;
                }
                if (strings.FindLocaleName("en-us", out uint index, out bool exists) < 0 || !exists)
                {
                    index = 0;
                }
                if (strings.GetStringLength(index, out uint length) < 0)
                {
                    continue;
                }
                var buffer = new StringBuilder((int)length + 1);
                if (strings.GetString(index, buffer, length + 1) >= 0)
                {
                    names.Add(buffer.ToString());
                }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or DllNotFoundException or EntryPointNotFoundException)
        {
            // The picker falls back to its built-in list.
        }
        return names;
    }
}
