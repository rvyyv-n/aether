using Aether.Core.Playback.Input;
using Aether_App.Input;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.System;
using Windows.UI.Core;

namespace Aether_App.Settings.Pages;

public sealed partial class ShortcutsPage : Page
{
    private sealed class RowParts
    {
        public required ShortcutAction Action { get; init; }
        public required SettingsRow Row { get; init; }
        public required Ellipse ConflictDot { get; init; }
        public required StackPanel Keys { get; init; }
        public required TextBlock Prompt { get; init; }
        public required Button Change { get; init; }
        public required Button UseAlternative { get; init; }
        public required Button Reset { get; init; }
    }

    private readonly List<RowParts> _rows = [];
    private ShortcutAction? _recording;
    private bool _reserved;

    public ShortcutsPage()
    {
        InitializeComponent();
        var binder = new SettingsPageBinder(this);
        foreach (ShortcutEntry entry in App.Shortcuts.Entries)
        {
            RowParts parts = CreateRow(entry);
            _rows.Add(parts);
            ShortcutGroup.Children.Add(parts.Row);
        }

        ResetAllButton.Content = AppStrings.Get("Settings_Shortcuts_ResetAll");
        ResetAllButton.Click += (_, _) =>
        {
            _recording = null;
            App.Shortcuts.ResetAll();
            Refresh();
        };

        // The recorder has to see Escape and the other keys before the settings view does (Escape closes settings).
        PreviewKeyDown += OnPreviewKeyDown;
        binder.Track(Refresh);
    }

    private RowParts CreateRow(ShortcutEntry entry)
    {
        var resources = Application.Current.Resources;
        var dot = new Ellipse
        {
            Width = 8,
            Height = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)resources["AetherStatusWarningBrush"],
            Visibility = Visibility.Collapsed,
        };
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var prompt = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)resources["AetherCaptionTextStyle"],
            Foreground = (Brush)resources["AetherTextSecondaryBrush"],
            Visibility = Visibility.Collapsed,
        };
        var change = new Button { Style = (Style)resources["AetherOutlinePillButtonStyle"] };
        var useAlternative = new Button { Style = (Style)resources["AetherOutlinePillButtonStyle"], Visibility = Visibility.Collapsed };
        var reset = new Button
        {
            Style = (Style)resources["AetherIconButtonStyle"],
            Content = new FontIcon { FontFamily = (FontFamily)resources["SymbolThemeFontFamily"], FontSize = 14, Glyph = "" },
            Visibility = Visibility.Collapsed,
        };
        string resetLabel = AppStrings.Get("Settings_ResetToDefault");
        ToolTipService.SetToolTip(reset, resetLabel);
        AutomationProperties.SetName(reset, $"{resetLabel}: {entry.Name}");
        AutomationProperties.SetName(change, $"{AppStrings.Get("Settings_Shortcuts_Change")}: {entry.Name}");

        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        content.Children.Add(dot);
        content.Children.Add(keys);
        content.Children.Add(prompt);
        content.Children.Add(change);
        content.Children.Add(useAlternative);
        content.Children.Add(reset);

        var parts = new RowParts
        {
            Action = entry.Action,
            Row = new SettingsRow { Header = entry.Name, Content = content },
            ConflictDot = dot,
            Keys = keys,
            Prompt = prompt,
            Change = change,
            UseAlternative = useAlternative,
            Reset = reset,
        };
        change.Click += (_, _) => ToggleRecording(parts.Action);
        // Tabbing away from a recording button is a change of mind, not a key to bind.
        change.LostFocus += (_, _) =>
        {
            if (_recording == parts.Action)
            {
                _recording = null;
                Refresh();
            }
        };
        useAlternative.Click += (_, _) =>
        {
            App.Shortcuts.UseAlternative(parts.Action);
            Refresh();
        };
        reset.Click += (_, _) =>
        {
            App.Shortcuts.Reset(parts.Action);
            Refresh();
        };
        return parts;
    }

    private void ToggleRecording(ShortcutAction action)
    {
        _reserved = false;
        _recording = _recording == action ? null : action;
        Refresh();
    }

    private void Refresh()
    {
        foreach (RowParts parts in _rows)
        {
            ShortcutEntry entry = App.Shortcuts.Get(parts.Action);
            bool recording = _recording == parts.Action;

            parts.Row.Header = entry.Name;
            parts.Row.Description = entry.HasConflict
                ? AppStrings.Format("Settings_Shortcuts_Conflict", string.Join(", ", entry.ConflictsWith))
                : null;
            parts.ConflictDot.Visibility = entry.HasConflict ? Visibility.Visible : Visibility.Collapsed;

            parts.Keys.Children.Clear();
            foreach (KeyCombination binding in entry.Bindings)
            {
                parts.Keys.Children.Add(new KeycapStrip { Keys = binding.Keycaps });
            }
            parts.Prompt.Text = recording
                ? AppStrings.Get(_reserved ? "Settings_Shortcuts_Reserved" : "Settings_Shortcuts_PressKeyCombination")
                : AppStrings.Get("Settings_Shortcuts_Unbound");
            parts.Prompt.Visibility = recording || entry.IsUnbound ? Visibility.Visible : Visibility.Collapsed;
            parts.Keys.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;

            parts.Change.Content = AppStrings.Get(recording ? "Common_Cancel" : "Settings_Shortcuts_Change");
            parts.Reset.Visibility = entry.IsDefault ? Visibility.Collapsed : Visibility.Visible;

            if (entry.Alternative is { } alternative)
            {
                string label = AppStrings.Format("Settings_Shortcuts_UseAlternative", alternative.DisplayText);
                parts.UseAlternative.Content = label;
                AutomationProperties.SetName(parts.UseAlternative, $"{label}: {entry.Name}");
                parts.UseAlternative.Visibility = !recording && !entry.Bindings.Contains(alternative) ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_recording is not { } action)
        {
            return;
        }
        int key = (int)e.Key;
        if (key == KeyCombination.Escape)
        {
            e.Handled = true;
            _recording = null;
            Refresh();
            return;
        }
        // Modifier keys on their own, Tab and the like cannot be bound: the combination is still being built or focus is moving on.
        if (!KeyCombination.IsBindableKey(key) && key is not (KeyCombination.OemComma or KeyCombination.OemPeriod))
        {
            return;
        }

        e.Handled = true;
        var combination = new KeyCombination(key, CurrentModifiers());
        if (combination.IsReserved)
        {
            _reserved = true;
            Refresh();
            return;
        }
        _recording = null;
        App.Shortcuts.Rebind(action, [combination]);
        Refresh();
    }

    private static KeyModifiers CurrentModifiers()
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
