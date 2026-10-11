using Aether.Core.Interop.Native;

namespace Aether.Core.Interop;

/// <summary>
/// Hides the mouse cursor over the calling thread's windows (WinUI has no hidden cursor shape).
/// <c>ShowCursor</c> keeps a per-thread counter, so this tracks its own state and steps the counter
/// only across the shown/hidden boundary. Call from the UI thread only.
/// </summary>
public static class CursorVisibility
{
    // Bounds the counter walk if something else on the thread also calls ShowCursor.
    private const int MaxSteps = 16;

    [ThreadStatic]
    private static bool t_hidden;

    public static bool IsHidden => t_hidden;

    public static void Hide()
    {
        if (t_hidden)
        {
            return;
        }
        t_hidden = true;
        for (int i = 0; i < MaxSteps && User32.ShowCursor(0) >= 0; i++)
        {
        }
    }

    public static void Show()
    {
        if (!t_hidden)
        {
            return;
        }
        t_hidden = false;
        for (int i = 0; i < MaxSteps && User32.ShowCursor(1) < 0; i++)
        {
        }
    }
}
