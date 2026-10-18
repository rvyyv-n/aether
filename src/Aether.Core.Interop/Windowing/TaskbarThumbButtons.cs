using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Aether.Core.Interop.DirectX.Native;
using Aether.Core.Interop.Native;

namespace Aether.Core.Interop.Windowing;

/// <summary>Which picture a taskbar thumbnail button shows.</summary>
public enum TaskbarGlyph
{
    Previous,
    Play,
    Pause,
    Next,
}

/// <summary>The three buttons, numbered as the window procedure receives them.</summary>
public enum TaskbarButton
{
    Previous = 1,
    PlayPause = 2,
    Next = 3,
}

/// <summary>
/// Previous, Play/Pause and next buttons under the window's taskbar preview (<c>ITaskbarList3</c> thumbnail
/// toolbar). Windows only accepts the buttons once the taskbar has made the window's button, so they are added
/// when the registered <c>TaskbarButtonCreated</c> message arrives, and again if Explorer restarts.
/// <para>
/// The taskbar object is held as a bare COM pointer and called through its vtable, like the DirectX objects
/// (ADR-0002). The icons are glyphs from Segoe MDL2 Assets drawn once with GDI into a 32-bit bitmap, so nothing
/// is shipped as an asset and the colour can follow the taskbar theme.
/// </para>
/// <para>
/// Threading: create, update and dispose on the window's thread; clicks are raised there too. It subclasses the
/// window procedure (chaining to the one before it) and keeps its state in static fields, so one per process.
/// </para>
/// </summary>
public sealed unsafe class TaskbarThumbButtons : IDisposable
{
    private const uint ThbIcon = 0x2;
    private const uint ThbTooltip = 0x4;
    private const uint ThbFlags = 0x8;
    private const uint ThbfDisabled = 0x1;
    private const uint ThbnClicked = 0x1800;
    private const int TipLength = 260;
    private const int GlyphCount = 4;
    private const int ButtonCount = 3;

    // ITaskbarList / ITaskbarList3 vtable slots after IUnknown.
    private const int HrInitSlot = 3;
    private const int ThumbBarAddButtonsSlot = 15;
    private const int ThumbBarUpdateButtonsSlot = 16;

    private static readonly Guid ClsidTaskbarList = new("56FDF344-FD6D-11d0-958A-006097C9A090");
    private static readonly Guid IidTaskbarList3 = new("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEFAF");
    private static readonly char[] GlyphChars = ['', '', '', ''];

    private static nint s_hwnd;
    private static nint s_previous;
    private static uint s_buttonCreatedMessage;
    private static TaskbarThumbButtons? s_instance;

    private readonly nint _hwnd;
    private readonly string _previousTip;
    private readonly string _nextTip;
    private readonly Action<TaskbarButton> _clicked;
    private readonly nint[] _icons = new nint[GlyphCount];
    private nint _taskbar;
    private bool _added;
    private bool _lightTheme;
    private bool _disposed;

    private bool _previousEnabled;
    private bool _playPauseEnabled;
    private bool _nextEnabled;
    private TaskbarGlyph _playPauseGlyph = TaskbarGlyph.Play;
    private string _playPauseTip = string.Empty;

    /// <param name="lightTheme">The taskbar is light, so the glyphs are dark.</param>
    public TaskbarThumbButtons(nint hwnd, string previousTip, string nextTip, bool lightTheme, Action<TaskbarButton> clicked)
    {
        ArgumentNullException.ThrowIfNull(previousTip);
        ArgumentNullException.ThrowIfNull(nextTip);
        ArgumentNullException.ThrowIfNull(clicked);
        if (hwnd == 0)
        {
            throw new ArgumentException("A window handle is required.", nameof(hwnd));
        }
        if (s_hwnd != 0)
        {
            throw new InvalidOperationException("Only one window can have taskbar buttons at a time.");
        }

        _hwnd = hwnd;
        _previousTip = previousTip;
        _nextTip = nextTip;
        _lightTheme = lightTheme;
        _clicked = clicked;

        nint procedure = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        nint previous = User32.SetWindowLongPtr(hwnd, User32.GWLP_WNDPROC, procedure);
        if (previous == 0)
        {
            throw new InvalidOperationException($"SetWindowLongPtr(GWLP_WNDPROC) failed: {Marshal.GetLastPInvokeError()}.");
        }
        s_hwnd = hwnd;
        s_previous = previous;
        s_buttonCreatedMessage = User32.RegisterWindowMessage("TaskbarButtonCreated");
        s_instance = this;

        // The message may already have been sent before we subclassed; adding early is harmless if it fails.
        TryAddButtons();
    }

    /// <summary>Sets what the buttons show; does nothing when it is what they already show.</summary>
    public void Update(bool previousEnabled, TaskbarGlyph playPauseGlyph, string playPauseTip, bool playPauseEnabled, bool nextEnabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_previousEnabled == previousEnabled && _playPauseGlyph == playPauseGlyph && _playPauseEnabled == playPauseEnabled
            && _nextEnabled == nextEnabled && string.Equals(_playPauseTip, playPauseTip, StringComparison.Ordinal))
        {
            return;
        }
        _previousEnabled = previousEnabled;
        _playPauseGlyph = playPauseGlyph;
        _playPauseTip = playPauseTip;
        _playPauseEnabled = playPauseEnabled;
        _nextEnabled = nextEnabled;
        PushButtons();
    }

    /// <summary>Draws the glyphs again for a light or dark taskbar.</summary>
    public void SetLightTheme(bool lightTheme)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lightTheme == lightTheme)
        {
            return;
        }
        _lightTheme = lightTheme;
        if (_added)
        {
            DestroyIcons();
            PushButtons();
        }
    }

    /// <summary>Restores the original window procedure, unless something has subclassed the window after us.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        s_instance = null;
        Com.Release(ref _taskbar);
        DestroyIcons();
        if (s_hwnd != _hwnd)
        {
            return; // the window is already gone
        }
        nint ours = (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint, nint, nint>)&WindowProcedure;
        if (User32.GetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC) == ours)
        {
            User32.SetWindowLongPtr(_hwnd, User32.GWLP_WNDPROC, s_previous);
            s_hwnd = 0;
        }
        // Otherwise leave the chain intact: with no instance our procedure only forwards.
    }

    /// <summary>A fresh taskbar object each time: after Explorer restarts the old one points at nothing.</summary>
    private void TryAddButtons()
    {
        Com.Release(ref _taskbar);
        _added = false;
        DestroyIcons();

        Guid clsid = ClsidTaskbarList;
        Guid iid = IidTaskbarList3;
        nint taskbar = 0;
        if (Ole32.CoCreateInstance(&clsid, 0, Ole32.CLSCTX_INPROC_SERVER, &iid, &taskbar) < 0 || taskbar == 0)
        {
            return;
        }
        if (((delegate* unmanaged[Stdcall]<nint, int>)Com.Vtbl(taskbar)[HrInitSlot])(taskbar) < 0)
        {
            Com.Release(taskbar);
            return;
        }
        _taskbar = taskbar;

        ThumbButton* buttons = stackalloc ThumbButton[ButtonCount];
        Fill(buttons, ThbIcon | ThbTooltip | ThbFlags);
        int hr = ((delegate* unmanaged[Stdcall]<nint, nint, uint, ThumbButton*, int>)Com.Vtbl(taskbar)[ThumbBarAddButtonsSlot])(
            taskbar, _hwnd, ButtonCount, buttons);
        if (hr < 0)
        {
            Com.Release(ref _taskbar);
            DestroyIcons();
            return;
        }
        _added = true;
    }

    private void PushButtons()
    {
        if (!_added)
        {
            return;
        }
        ThumbButton* buttons = stackalloc ThumbButton[ButtonCount];
        Fill(buttons, ThbIcon | ThbTooltip | ThbFlags);
        ((delegate* unmanaged[Stdcall]<nint, nint, uint, ThumbButton*, int>)Com.Vtbl(_taskbar)[ThumbBarUpdateButtonsSlot])(
            _taskbar, _hwnd, ButtonCount, buttons);
    }

    private void Fill(ThumbButton* buttons, uint mask)
    {
        Describe(&buttons[0], mask, TaskbarButton.Previous, TaskbarGlyph.Previous, _previousTip, _previousEnabled);
        Describe(&buttons[1], mask, TaskbarButton.PlayPause, _playPauseGlyph, _playPauseTip, _playPauseEnabled);
        Describe(&buttons[2], mask, TaskbarButton.Next, TaskbarGlyph.Next, _nextTip, _nextEnabled);
    }

    private void Describe(ThumbButton* button, uint mask, TaskbarButton id, TaskbarGlyph glyph, string tip, bool enabled)
    {
        *button = default;
        button->Mask = mask;
        button->Id = (uint)id;
        button->Icon = IconFor(glyph);
        tip.AsSpan(0, Math.Min(tip.Length, TipLength - 1)).CopyTo(new Span<char>(button->Tip, TipLength));
        button->Flags = enabled ? 0 : ThbfDisabled;
    }

    private nint IconFor(TaskbarGlyph glyph)
    {
        ref nint icon = ref _icons[(int)glyph];
        if (icon == 0)
        {
            icon = CreateGlyphIcon(GlyphChars[(int)glyph], _lightTheme, (int)(16 * User32.GetDpiForWindow(_hwnd) / 96));
        }
        return icon;
    }

    private void DestroyIcons()
    {
        for (int i = 0; i < _icons.Length; i++)
        {
            if (_icons[i] != 0)
            {
                User32.DestroyIcon(_icons[i]);
                _icons[i] = 0;
            }
        }
    }

    /// <summary>
    /// Draws the glyph white on black with grayscale smoothing, then keeps the smoothing as the alpha of a solid
    /// foreground colour: GDI text writes no alpha of its own.
    /// </summary>
    private static nint CreateGlyphIcon(char glyph, bool darkGlyph, int size)
    {
        size = Math.Max(16, size);
        nint dc = Gdi32.CreateCompatibleDC(0);
        if (dc == 0)
        {
            return 0;
        }
        Gdi32.BitmapInfoHeader header = new()
        {
            Size = (uint)sizeof(Gdi32.BitmapInfoHeader),
            Width = size,
            Height = -size, // top-down
            Planes = 1,
            BitCount = 32,
        };
        byte* pixels = null;
        nint color = Gdi32.CreateDIBSection(dc, &header, 0, &pixels, 0, 0);
        nint font = Gdi32.CreateFont(
            -size, 0, 0, 0, Gdi32.FW_NORMAL, 0, 0, 0, Gdi32.DEFAULT_CHARSET, 0, 0, Gdi32.ANTIALIASED_QUALITY, 0, "Segoe MDL2 Assets");
        nint mask = 0;
        nint icon = 0;
        try
        {
            if (color == 0 || pixels == null || font == 0)
            {
                return 0;
            }
            new Span<byte>(pixels, size * size * 4).Clear();
            nint previousBitmap = Gdi32.SelectObject(dc, color);
            nint previousFont = Gdi32.SelectObject(dc, font);
            Gdi32.SetBkMode(dc, Gdi32.TRANSPARENT);
            Gdi32.SetTextColor(dc, 0xFFFFFF);
            Gdi32.Size extent = default;
            Gdi32.GetTextExtentPoint32(dc, &glyph, 1, &extent);
            Gdi32.TextOut(dc, (size - extent.Width) / 2, (size - extent.Height) / 2, &glyph, 1);
            Gdi32.SelectObject(dc, previousFont);
            Gdi32.SelectObject(dc, previousBitmap);

            byte value = darkGlyph ? (byte)0x1A : (byte)0xFF;
            uint* argb = (uint*)pixels;
            for (int i = 0; i < size * size; i++)
            {
                uint alpha = (argb[i] >> 8) & 0xFF; // the green channel carries the smoothing
                argb[i] = (alpha << 24) | ((uint)value << 16) | ((uint)value << 8) | value;
            }

            // A colour icon with alpha still needs a mask bitmap; all zeros means "draw everything".
            byte* maskBits = stackalloc byte[((size + 15) / 16) * 2 * size];
            new Span<byte>(maskBits, ((size + 15) / 16) * 2 * size).Clear();
            mask = Gdi32.CreateBitmap(size, size, 1, 1, maskBits);
            User32.IconInfo info = new() { IsIcon = 1, Mask = mask, Color = color };
            icon = User32.CreateIconIndirect(&info);
        }
        finally
        {
            if (mask != 0)
            {
                Gdi32.DeleteObject(mask);
            }
            if (font != 0)
            {
                Gdi32.DeleteObject(font);
            }
            if (color != 0)
            {
                Gdi32.DeleteObject(color);
            }
            Gdi32.DeleteDC(dc);
        }
        return icon;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static nint WindowProcedure(nint hwnd, uint message, nint wParam, nint lParam)
    {
        if (hwnd == s_hwnd)
        {
            if (message == User32.WM_COMMAND && ((uint)wParam >> 16) == ThbnClicked && s_instance is { } instance)
            {
                int id = (int)((uint)wParam & 0xFFFF);
                if (id is >= (int)TaskbarButton.Previous and <= (int)TaskbarButton.Next)
                {
                    try
                    {
                        instance._clicked((TaskbarButton)id);
                    }
                    catch
                    {
                        // An exception must not unwind into native code.
                    }
                    return 0;
                }
            }
            else if (message == s_buttonCreatedMessage && s_instance is { } owner)
            {
                try
                {
                    owner.TryAddButtons();
                }
                catch
                {
                    // The window works without the buttons.
                }
            }
            else if (message == User32.WM_NCDESTROY)
            {
                // The window is going away, and a procedure beneath us may already have unhooked itself.
                s_instance = null;
                s_hwnd = 0;
            }
        }
        return User32.CallWindowProc(s_previous, hwnd, message, wParam, lParam);
    }

    /// <summary>Win32 <c>THUMBBUTTON</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ThumbButton
    {
        public uint Mask;
        public uint Id;
        public uint Bitmap;
        public nint Icon;
        public fixed char Tip[TipLength];
        public uint Flags;
    }
}
