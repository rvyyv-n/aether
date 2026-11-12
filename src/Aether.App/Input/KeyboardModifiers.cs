using Aether.Core.Playback.Input;
using Microsoft.UI.Input;
using Windows.System;
using Windows.UI.Core;

namespace Aether_App.Input;

/// <summary>The Ctrl, Alt and Shift keys held right now, for a key read outside a keyboard accelerator.</summary>
internal static class KeyboardModifiers
{
    public static KeyModifiers Current()
    {
        static bool Down(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
        KeyModifiers modifiers = KeyModifiers.None;
        if (Down(VirtualKey.Control))
        {
            modifiers |= KeyModifiers.Ctrl;
        }
        if (Down(VirtualKey.Menu))
        {
            modifiers |= KeyModifiers.Alt;
        }
        if (Down(VirtualKey.Shift))
        {
            modifiers |= KeyModifiers.Shift;
        }
        return modifiers;
    }
}
