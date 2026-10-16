using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Input;

/// <summary>
/// Which keys trigger which action. Immutable: every change returns a new map. Saved as one text setting per
/// action: empty means the default keys, <c>None</c> means unbound, otherwise the combinations separated by
/// commas (<c>F, F11</c>). A saved value that cannot be read gives that action its defaults.
/// </summary>
public sealed class ShortcutMap
{
    private const string UnboundText = "None";

    private readonly IReadOnlyList<KeyCombination>[] _bindings;

    private ShortcutMap(IReadOnlyList<KeyCombination>[] bindings) => _bindings = bindings;

    public static ShortcutMap Defaults { get; } = new([.. ShortcutActions.All.Select(ShortcutActions.Defaults)]);

    public static ShortcutMap From(AetherSettings settings) =>
        new([.. ShortcutActions.All.Select(a => Decode(SettingText(settings, a)) ?? ShortcutActions.Defaults(a))]);

    public IReadOnlyList<KeyCombination> Get(ShortcutAction action) => _bindings[(int)action];

    public bool IsDefault(ShortcutAction action) => Get(action).SequenceEqual(ShortcutActions.Defaults(action));

    /// <summary>Replaces the action's keys. Invalid combinations and repeats are dropped; an empty list unbinds it.</summary>
    public ShortcutMap With(ShortcutAction action, IEnumerable<KeyCombination> bindings)
    {
        var next = (IReadOnlyList<KeyCombination>[])_bindings.Clone();
        next[(int)action] = [.. bindings.Where(b => b.IsValid).Distinct()];
        return new ShortcutMap(next);
    }

    public ShortcutMap Reset(ShortcutAction action) => With(action, ShortcutActions.Defaults(action));

    public ShortcutMap ResetAll() => Defaults;

    /// <summary>The first action (in declaration order) the combination triggers, or null.</summary>
    public ShortcutAction? Find(KeyCombination combination)
    {
        foreach (ShortcutAction action in ShortcutActions.All)
        {
            if (Get(action).Contains(combination))
            {
                return action;
            }
        }
        return null;
    }

    /// <summary>The other actions that share at least one combination with this one.</summary>
    public IReadOnlyList<ShortcutAction> ConflictsOf(ShortcutAction action) =>
        [.. ShortcutActions.All.Where(other => other != action && Get(other).Intersect(Get(action)).Any())];

    public bool HasConflict(ShortcutAction action) => ConflictsOf(action).Count > 0;

    /// <summary>The value to store in the action's setting. Default keys give an empty value.</summary>
    public string Encode(ShortcutAction action)
    {
        if (IsDefault(action))
        {
            return "";
        }
        IReadOnlyList<KeyCombination> bindings = Get(action);
        return bindings.Count == 0 ? UnboundText : string.Join(", ", bindings);
    }

    /// <summary>Writes every action's keys to the store, which raises its change event for each one that changed.</summary>
    public void WriteTo(SettingsStore store)
    {
        foreach (ShortcutAction action in ShortcutActions.All)
        {
            store.Set(ShortcutActions.SettingKey(action), Encode(action));
        }
    }

    /// <summary>True when the saved text is empty, <c>None</c> or a list of known combinations.</summary>
    public static bool IsValidText(string text) => text.Length == 0 || Decode(text) is not null;

    /// <summary>Null when the text is empty (use the defaults) or cannot be read.</summary>
    private static IReadOnlyList<KeyCombination>? Decode(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }
        if (text.Trim().Equals(UnboundText, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }
        var result = new List<KeyCombination>();
        foreach (string part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (!KeyCombination.TryParse(part, out KeyCombination combination))
            {
                return null;
            }
            if (!result.Contains(combination))
            {
                result.Add(combination);
            }
        }
        return result;
    }

    private static string SettingText(AetherSettings settings, ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFile => settings.ShortcutOpenFile,
        ShortcutAction.OpenUrl => settings.ShortcutOpenUrl,
        ShortcutAction.SkipBack => settings.ShortcutSkipBack,
        ShortcutAction.SkipForward => settings.ShortcutSkipForward,
        ShortcutAction.SkipBackFine => settings.ShortcutSkipBackFine,
        ShortcutAction.SkipForwardFine => settings.ShortcutSkipForwardFine,
        ShortcutAction.SkipBackLong => settings.ShortcutSkipBackLong,
        ShortcutAction.SkipForwardLong => settings.ShortcutSkipForwardLong,
        ShortcutAction.SetIn => settings.ShortcutSetIn,
        ShortcutAction.SetOut => settings.ShortcutSetOut,
        ShortcutAction.PlayPause => settings.ShortcutPlayPause,
        ShortcutAction.FullScreen => settings.ShortcutFullScreen,
        ShortcutAction.Mute => settings.ShortcutMute,
        ShortcutAction.Loop => settings.ShortcutLoop,
        ShortcutAction.AlwaysOnTop => settings.ShortcutAlwaysOnTop,
        ShortcutAction.PictureInPicture => settings.ShortcutPictureInPicture,
        ShortcutAction.Diagnostics => settings.ShortcutDiagnostics,
        ShortcutAction.Settings => settings.ShortcutSettings,
        ShortcutAction.CopyFrame => settings.ShortcutCopyFrame,
        ShortcutAction.SaveFrame => settings.ShortcutSaveFrame,
        ShortcutAction.ExportClip => settings.ShortcutExportClip,
        _ => "",
    };
}
