using System.Text;

namespace Aether.Core.Playback.Input;

/// <summary>
/// A key plus modifiers, using Windows virtual-key codes so it needs no UI types. The text form
/// (<c>Ctrl+Shift+O</c>) is what gets saved; <see cref="Keycaps"/> is what the shortcuts page shows.
/// </summary>
public readonly record struct KeyCombination(int Key, KeyModifiers Modifiers = KeyModifiers.None)
{
    private static readonly (int Key, string Name, string Cap)[] Names = BuildNames();
    private static readonly Dictionary<int, (string Name, string Cap)> ByKey = Names.ToDictionary(n => n.Key, n => (n.Name, n.Cap));
    private static readonly Dictionary<string, int> ByName = Names.ToDictionary(n => n.Name, n => n.Key, StringComparer.OrdinalIgnoreCase);

    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Right = 0x27;
    public const int Escape = 0x1B;
    public const int F11 = 0x7A;
    public const int OemComma = 0xBC;
    public const int OemPeriod = 0xBE;

    public static KeyCombination Letter(char letter, KeyModifiers modifiers = KeyModifiers.None) =>
        new(char.ToUpperInvariant(letter), modifiers);

    /// <summary>True when the key has a saved name, so it can be stored and shown. Modifier keys alone have none.</summary>
    public static bool IsBindableKey(int key) => ByKey.ContainsKey(key);

    /// <summary>
    /// Keys the player keeps for itself: plain Escape (leave full screen or close) and comma and full stop (the
    /// frame-step keys, which are read as raw key events rather than accelerators) unless Ctrl is held, which
    /// frees Ctrl+, for Settings.
    /// </summary>
    public bool IsReserved =>
        (Key == Escape && Modifiers == KeyModifiers.None) || (Key is OemComma or OemPeriod && (Modifiers & KeyModifiers.Ctrl) == 0);

    public bool IsValid => IsBindableKey(Key) && !IsReserved;

    /// <summary>The stable saved form, e.g. <c>Ctrl+Shift+O</c>. Modifiers come first in Ctrl, Alt, Shift order.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        foreach (string modifier in ModifierNames(Modifiers))
        {
            text.Append(modifier).Append('+');
        }
        return text.Append(ByKey.TryGetValue(Key, out var name) ? name.Name : $"Key{Key}").ToString();
    }

    /// <summary>The pieces of a keycap row: <c>["Ctrl", "Shift", "O"]</c>, with arrows and punctuation as symbols.</summary>
    public IReadOnlyList<string> Keycaps
    {
        get
        {
            var caps = new List<string>(ModifierNames(Modifiers))
            {
                ByKey.TryGetValue(Key, out var name) ? name.Cap : $"Key{Key}",
            };
            return caps;
        }
    }

    /// <summary>One line of text for menus and tooltips, e.g. <c>Ctrl+Shift+O</c>.</summary>
    public string DisplayText => string.Join('+', Keycaps);

    /// <summary>Parses the saved form. Case-insensitive; returns false for anything that is not a known, unreserved key with known modifiers.</summary>
    public static bool TryParse(string? text, out KeyCombination combination)
    {
        combination = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);

        KeyModifiers modifiers = KeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            KeyModifiers next = parts[i].ToLowerInvariant() switch
            {
                "ctrl" or "control" => KeyModifiers.Ctrl,
                "shift" => KeyModifiers.Shift,
                "alt" => KeyModifiers.Alt,
                _ => KeyModifiers.None,
            };
            if (next == KeyModifiers.None || (modifiers & next) != 0)
            {
                return false;
            }
            modifiers |= next;
        }
        if (!ByName.TryGetValue(parts[^1], out int key))
        {
            return false;
        }
        combination = new KeyCombination(key, modifiers);
        return combination.IsValid;
    }

    private static IEnumerable<string> ModifierNames(KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Ctrl) != 0)
        {
            yield return "Ctrl";
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            yield return "Alt";
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            yield return "Shift";
        }
    }

    private static (int, string, string)[] BuildNames()
    {
        var names = new List<(int, string, string)>();
        for (char c = 'A'; c <= 'Z'; c++)
        {
            names.Add((c, c.ToString(), c.ToString()));
        }
        for (char c = '0'; c <= '9'; c++)
        {
            names.Add((c, c.ToString(), c.ToString()));
        }
        for (int i = 1; i <= 24; i++)
        {
            names.Add((0x6F + i, $"F{i}", $"F{i}"));
        }
        for (int i = 0; i <= 9; i++)
        {
            names.Add((0x60 + i, $"Num{i}", $"Num {i}"));
        }
        names.AddRange(
        [
            (0x08, "Backspace", "Backspace"),
            (0x09, "Tab", "Tab"),
            (0x0D, "Enter", "Enter"),
            (Escape, "Escape", "Esc"),
            (Space, "Space", "Space"),
            (0x21, "PageUp", "Page Up"),
            (0x22, "PageDown", "Page Down"),
            (0x23, "End", "End"),
            (0x24, "Home", "Home"),
            (Left, "Left", "←"),
            (0x26, "Up", "↑"),
            (Right, "Right", "→"),
            (0x28, "Down", "↓"),
            (0x2D, "Insert", "Insert"),
            (0x2E, "Delete", "Delete"),
            (0xBA, "Semicolon", ";"),
            (0xBB, "Equals", "="),
            (OemComma, "Comma", ","),
            (0xBD, "Minus", "-"),
            (OemPeriod, "Period", "."),
            (0xBF, "Slash", "/"),
            (0xC0, "Backquote", "`"),
            (0xDB, "OpenBracket", "["),
            (0xDC, "Backslash", "\\"),
            (0xDD, "CloseBracket", "]"),
            (0xDE, "Quote", "'"),
        ]);
        return [.. names];
    }
}
