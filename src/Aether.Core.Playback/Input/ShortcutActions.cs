using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Input;

/// <summary>Per-action facts: the saved setting key, the default keys and the one-click alternative.</summary>
public static class ShortcutActions
{
    public static IReadOnlyList<ShortcutAction> All { get; } = Enum.GetValues<ShortcutAction>();

    /// <summary>Plain L (loop) and Ctrl+L are different combinations, so this does not clash with Loop's default.</summary>
    public static KeyCombination OpenUrlAlternative { get; } = KeyCombination.Letter('L', KeyModifiers.Ctrl);

    /// <summary>The setting that stores this action's keys.</summary>
    public static string SettingKey(ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFile => SettingKeys.ShortcutOpenFile,
        ShortcutAction.OpenUrl => SettingKeys.ShortcutOpenUrl,
        ShortcutAction.SkipBack => SettingKeys.ShortcutSkipBack,
        ShortcutAction.SkipForward => SettingKeys.ShortcutSkipForward,
        ShortcutAction.SetIn => SettingKeys.ShortcutSetIn,
        ShortcutAction.SetOut => SettingKeys.ShortcutSetOut,
        ShortcutAction.PlayPause => SettingKeys.ShortcutPlayPause,
        ShortcutAction.FullScreen => SettingKeys.ShortcutFullScreen,
        ShortcutAction.Mute => SettingKeys.ShortcutMute,
        ShortcutAction.Loop => SettingKeys.ShortcutLoop,
        ShortcutAction.AlwaysOnTop => SettingKeys.ShortcutAlwaysOnTop,
        ShortcutAction.Diagnostics => SettingKeys.ShortcutDiagnostics,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    /// <summary>The action stored under a setting key, or null for any other key.</summary>
    public static ShortcutAction? FromSettingKey(string key)
    {
        foreach (ShortcutAction action in All)
        {
            if (string.Equals(SettingKey(action), key, StringComparison.Ordinal))
            {
                return action;
            }
        }
        return null;
    }

    /// <summary>The keys the action has until the user changes it. Full screen has two.</summary>
    public static IReadOnlyList<KeyCombination> Defaults(ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFile => [KeyCombination.Letter('O', KeyModifiers.Ctrl)],
        ShortcutAction.OpenUrl => [KeyCombination.Letter('O', KeyModifiers.Ctrl | KeyModifiers.Shift)],
        ShortcutAction.SkipBack => [new KeyCombination(KeyCombination.Left)],
        ShortcutAction.SkipForward => [new KeyCombination(KeyCombination.Right)],
        ShortcutAction.SetIn => [KeyCombination.Letter('I')],
        ShortcutAction.SetOut => [KeyCombination.Letter('O')],
        ShortcutAction.PlayPause => [new KeyCombination(KeyCombination.Space)],
        ShortcutAction.FullScreen => [KeyCombination.Letter('F'), new KeyCombination(KeyCombination.F11)],
        ShortcutAction.Mute => [KeyCombination.Letter('M')],
        ShortcutAction.Loop => [KeyCombination.Letter('L')],
        ShortcutAction.AlwaysOnTop => [KeyCombination.Letter('T')],
        ShortcutAction.Diagnostics => [KeyCombination.Letter('I', KeyModifiers.Ctrl)],
        _ => [],
    };

    /// <summary>A ready-made replacement offered next to the default, or null when the action has none.</summary>
    public static KeyCombination? Alternative(ShortcutAction action) =>
        action == ShortcutAction.OpenUrl ? OpenUrlAlternative : null;
}
