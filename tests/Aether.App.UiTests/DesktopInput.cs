using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Aether.App.UiTests;

/// <summary>
/// Dispatches keyboard and pointer input to the target window. Uses FlaUI's hardware <c>SendInput</c> path on
/// interactive desktops (local developer sessions and GitHub Actions <c>windows-latest</c>) and falls back to
/// targeted Win32 window messages when running on a non-active desktop where Windows rejects <c>SendInput</c>
/// with <c>ERROR_ACCESS_DENIED</c>.
/// </summary>
internal static class DesktopInput
{
    private const int ErrorAccessDenied = 5;
    private const uint InputMouse = 0;
    private const uint MouseEventMove = 0x0001;

    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201;
    private const uint WmLButtonUp = 0x0202;
    private const nint MkLButton = 0x0001;

    private static readonly Lazy<bool> s_hardwareInputAvailable = new(ProbeHardwareInput);

    public static bool IsHardwareInputAvailable => s_hardwareInputAvailable.Value;

    public static void PressKey(Window window, VirtualKeyShort key)
    {
        if (IsHardwareInputAvailable)
        {
            Keyboard.Press(key);
            return;
        }

        nint target = FindInputTarget(window);
        uint vk = (uint)key;
        uint scan = MapVirtualKey(vk, 0);
        nint downLParam = (nint)((scan << 16) | 1u);
        nint upLParam = unchecked((nint)(0xC0000001u | (scan << 16)));
        PostMessage(target, WmKeyDown, (nint)vk, downLParam);
        Thread.Sleep(25);
        PostMessage(target, WmKeyUp, (nint)vk, upLParam);
        Thread.Sleep(60);
    }

    public static void PressChord(Window window, VirtualKeyShort modifier, VirtualKeyShort key)
    {
        if (IsHardwareInputAvailable)
        {
            Keyboard.TypeSimultaneously(modifier, key);
            return;
        }

        nint target = FindInputTarget(window);
        uint targetTid = GetWindowThreadProcessId(target, out _);
        uint currentTid = GetCurrentThreadId();
        uint modVk = (uint)modifier;
        uint keyVk = (uint)key;
        uint modScan = MapVirtualKey(modVk, 0);
        uint keyScan = MapVirtualKey(keyVk, 0);

        byte[] savedState = new byte[256];
        byte[] chordState = new byte[256];
        bool attached = targetTid != 0 && targetTid != currentTid && AttachThreadInput(currentTid, targetTid, true);
        try
        {
            if (GetKeyboardState(savedState))
            {
                Buffer.BlockCopy(savedState, 0, chordState, 0, savedState.Length);
                chordState[modVk] |= 0x80;
                if (modifier == VirtualKeyShort.CONTROL)
                {
                    chordState[(int)VirtualKeyShort.LCONTROL] |= 0x80;
                }
                SetKeyboardState(chordState);
            }

            SendMessage(target, WmKeyDown, (nint)modVk, (nint)((modScan << 16) | 1u));
            SendMessage(target, WmKeyDown, (nint)keyVk, (nint)((keyScan << 16) | 1u));
            Thread.Sleep(50);
            SendMessage(target, WmKeyUp, (nint)keyVk, unchecked((nint)(0xC0000001u | (keyScan << 16))));
            SendMessage(target, WmKeyUp, (nint)modVk, unchecked((nint)(0xC0000001u | (modScan << 16))));
            Thread.Sleep(60);
        }
        finally
        {
            if (attached)
            {
                SetKeyboardState(savedState);
                AttachThreadInput(currentTid, targetTid, false);
            }
        }
    }

    public static void OpenContextMenu(Window window, Point screenPoint)
    {
        if (IsHardwareInputAvailable)
        {
            Mouse.RightClick(screenPoint);
            return;
        }

        // On non-active desktops where Windows blocks SendInput, VK_APPS triggers the same XAML ContextRequested path.
        PressKey(window, VirtualKeyShort.APPS);
    }

    public static void Drag(Window window, Point screenStart, Point screenEnd)
    {
        if (IsHardwareInputAvailable)
        {
            Mouse.Drag(screenStart, screenEnd);
            return;
        }

        nint target = FindInputTarget(window);
        Point start = ToClient(target, screenStart);
        Point end = ToClient(target, screenEnd);
        PostMessage(target, WmMouseMove, 0, MakeLParam(start.X, start.Y));
        Thread.Sleep(30);
        PostMessage(target, WmLButtonDown, MkLButton, MakeLParam(start.X, start.Y));
        Thread.Sleep(40);
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            int x = start.X + (end.X - start.X) * i / steps;
            int y = start.Y + (end.Y - start.Y) * i / steps;
            PostMessage(target, WmMouseMove, MkLButton, MakeLParam(x, y));
            Thread.Sleep(20);
        }
        PostMessage(target, WmLButtonUp, 0, MakeLParam(end.X, end.Y));
        Thread.Sleep(80);
    }

    private static bool ProbeHardwareInput()
    {
        var input = new RawInput
        {
            Type = InputMouse,
            Mouse = new RawMouseInput { Flags = MouseEventMove },
        };
        uint sent = SendInput(1, [input], Marshal.SizeOf<RawInput>());
        if (sent == 1)
        {
            return true;
        }
        int error = Marshal.GetLastWin32Error();
        if (error == ErrorAccessDenied)
        {
            return false;
        }
        throw new Win32Exception(error);
    }

    private static nint FindInputTarget(Window window)
    {
        nint main = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (main == 0)
        {
            return 0;
        }
        nint inputSite = FindChildByClass(main, "InputSiteWindowClass");
        if (inputSite != 0)
        {
            return inputSite;
        }
        return FindBridgeOrMain(window);
    }

    private static nint FindBridgeOrMain(Window window)
    {
        nint main = window.Properties.NativeWindowHandle.ValueOrDefault;
        if (main == 0)
        {
            return 0;
        }
        nint bridge = FindChildByClass(main, "Microsoft.UI.Content.DesktopChildSiteBridge");
        return bridge != 0 ? bridge : main;
    }

    private static nint FindChildByClass(nint parent, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (hwnd, _) =>
        {
            var sb = new StringBuilder(128);
            if (GetClassName(hwnd, sb, sb.Capacity) > 0 && string.Equals(sb.ToString(), className, StringComparison.Ordinal))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, 0);
        return found;
    }

    private static Point ToClient(nint hwnd, Point screen)
    {
        var pt = new NativePoint { X = screen.X, Y = screen.Y };
        ScreenToClient(hwnd, ref pt);
        return new Point(pt.X, pt.Y);
    }

    private static nint MakeLParam(int x, int y) => ((y & 0xFFFF) << 16) | (x & 0xFFFF);

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, RawInput[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint hWndParent, EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] lpKeyState);

    [DllImport("user32.dll")]
    private static extern bool SetKeyboardState(byte[] lpKeyState);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public uint Type;
        public RawMouseInput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }
}
