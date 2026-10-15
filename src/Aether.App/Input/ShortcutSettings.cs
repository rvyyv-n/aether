using Aether.Core.Playback.Input;
using Aether.Core.Playback.Settings;

namespace Aether_App.Input;

/// <summary>One row of the shortcuts page: an action, its keys now and by default, and whether another action shares them.</summary>
public sealed record ShortcutEntry(
    ShortcutAction Action,
    string Name,
    IReadOnlyList<KeyCombination> Bindings,
    IReadOnlyList<KeyCombination> Defaults,
    IReadOnlyList<string> ConflictsWith,
    KeyCombination? Alternative)
{
    public bool IsDefault => Bindings.SequenceEqual(Defaults);

    public bool HasConflict => ConflictsWith.Count > 0;

    public bool IsUnbound => Bindings.Count == 0;
}

/// <summary>
/// The player's shortcut keys as the shortcuts page and the main page see them, on top of the settings store.
/// Escape and the frame-step keys are not configurable and are not listed. Every change is saved at once.
/// </summary>
public sealed class ShortcutSettings
{
    private readonly SettingsStore _store;

    public ShortcutSettings(SettingsStore store)
    {
        _store = store;
        _store.Changed += OnStoreChanged;
    }

    /// <summary>Raised when any shortcut changes, on the thread that changed it (the thread pool while settings load).</summary>
    public event EventHandler? Changed;

    public ShortcutMap Map => ShortcutMap.From(_store.Current);

    /// <summary>Every configurable action, in the order the page lists them.</summary>
    public IReadOnlyList<ShortcutEntry> Entries
    {
        get
        {
            ShortcutMap map = Map;
            return [.. ShortcutActions.All.Select(a => Entry(map, a))];
        }
    }

    public ShortcutEntry Get(ShortcutAction action) => Entry(Map, action);

    /// <summary>Replaces the action's keys; an empty list unbinds it. Conflicts are allowed and flagged on the entry.</summary>
    public void Rebind(ShortcutAction action, IEnumerable<KeyCombination> bindings) =>
        Apply(Map.With(action, bindings), action);

    public void Reset(ShortcutAction action) => Apply(Map.Reset(action), action);

    public void ResetAll()
    {
        ShortcutMap.Defaults.WriteTo(_store);
        Save();
    }

    /// <summary>Swaps in the action's one-click alternative (Open URL: Ctrl+L). False when the action has none.</summary>
    public bool UseAlternative(ShortcutAction action)
    {
        if (ShortcutActions.Alternative(action) is not { } alternative)
        {
            return false;
        }
        Rebind(action, [alternative]);
        return true;
    }

    /// <summary>The first key of the action as keycap text for menus and tooltips (<c>Ctrl+O</c>), or empty when unbound.</summary>
    public string Describe(ShortcutAction action) =>
        Map.Get(action) is [var first, ..] ? first.DisplayText : "";

    /// <summary>Puts the action's current key at the end of a resource label such as <c>Loop (L)</c>, replacing the key it names.</summary>
    public string Tip(string label, ShortcutAction action)
    {
        int open = label.LastIndexOf(" (", StringComparison.Ordinal);
        string name = open > 0 ? label[..open] : label;
        string keys = Describe(action);
        return keys.Length == 0 ? name : $"{name} ({keys})";
    }

    private void Apply(ShortcutMap map, ShortcutAction action)
    {
        _store.Set(ShortcutActions.SettingKey(action), map.Encode(action));
        Save();
    }

    private void Save() => _ = Task.Run(() =>
    {
        try
        {
            _store.Save();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The change still holds this session; it is saved with the next one.
        }
    });

    private void OnStoreChanged(object? sender, SettingChangedEventArgs e)
    {
        if (ShortcutActions.FromSettingKey(e.Key) is not null)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static ShortcutEntry Entry(ShortcutMap map, ShortcutAction action) => new(
        action,
        AppStrings.Get(NameKey(action)),
        map.Get(action),
        ShortcutActions.Defaults(action),
        [.. map.ConflictsOf(action).Select(other => AppStrings.Get(NameKey(other)))],
        ShortcutActions.Alternative(action));

    private static string NameKey(ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFile => "Settings_Shortcuts_OpenFile",
        ShortcutAction.OpenUrl => "Settings_Shortcuts_OpenUrl",
        ShortcutAction.SkipBack => "Settings_Shortcuts_Back5s",
        ShortcutAction.SkipForward => "Settings_Shortcuts_Forward5s",
        ShortcutAction.SetIn => "Settings_Shortcuts_SetIn",
        ShortcutAction.SetOut => "Settings_Shortcuts_SetOut",
        ShortcutAction.PlayPause => "Settings_Shortcuts_PlayPause",
        ShortcutAction.FullScreen => "Settings_Shortcuts_FullScreen",
        ShortcutAction.Mute => "Settings_Shortcuts_Mute",
        ShortcutAction.Loop => "Settings_Shortcuts_Loop",
        ShortcutAction.AlwaysOnTop => "Settings_Shortcuts_AlwaysOnTop",
        ShortcutAction.Diagnostics => "Settings_Shortcuts_Diagnostics",
        ShortcutAction.Settings => "Settings_Shortcuts_Settings",
        ShortcutAction.CopyFrame => "Settings_Shortcuts_CopyFrame",
        ShortcutAction.SaveFrame => "Settings_Shortcuts_SaveFrame",
        ShortcutAction.ExportClip => "Settings_Shortcuts_ExportClip",
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };
}
