using System.Globalization;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;

namespace Aether_App.Settings;

/// <summary>A value kept in the settings store and the words shown for it.</summary>
internal readonly record struct Choice(string Value, string Label);

/// <summary>Writes the settings file off the UI thread; a burst of changes (a slider drag) becomes one or two writes.</summary>
internal static class SettingsSaver
{
    private static int _dirty;
    private static int _busy;

    public static void Save()
    {
        Interlocked.Exchange(ref _dirty, 1);
        if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
        {
            _ = Task.Run(Drain);
        }
    }

    private static void Drain()
    {
        do
        {
            while (Interlocked.Exchange(ref _dirty, 0) == 1)
            {
                try
                {
                    App.Settings.Save();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // The change still holds this session; it is saved with the next one.
                }
            }
            Volatile.Write(ref _busy, 0);
        }
        // A save requested between the last check and clearing the flag would otherwise be left unwritten.
        while (Volatile.Read(ref _dirty) == 1 && Interlocked.CompareExchange(ref _busy, 1, 0) == 0);
    }
}

/// <summary>
/// Ties a settings page's controls to <see cref="App.Settings"/>: fills each control from the store, writes
/// changes back, and keeps the page current when a setting changes elsewhere. UI thread only.
/// </summary>
internal sealed class SettingsPageBinder
{
    private readonly FrameworkElement _page;
    private readonly List<Action> _refreshers = [];
    private bool _updating;

    public SettingsPageBinder(FrameworkElement page)
    {
        _page = page;
        page.Loaded += OnLoaded;
        page.Unloaded += OnUnloaded;
    }

    /// <summary>Runs <paramref name="refresh"/> now and whenever any setting changes. Setting a control from it does not write back.</summary>
    public void Track(Action refresh)
    {
        _refreshers.Add(refresh);
        Run(refresh);
    }

    public static void Describe(SettingsRow row, string titleKey, string? descriptionKey)
    {
        row.Header = AppStrings.Get(titleKey);
        row.Description = descriptionKey is null ? null : AppStrings.Get(descriptionKey);
    }

    public void Toggle(SettingsRow row, ToggleSwitch toggle, string key, string titleKey, string? descriptionKey = null)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(toggle, row.Header);
        void ShowState() => row.StateText = AppStrings.Get(toggle.IsOn ? "Common_On" : "Common_Off");
        toggle.Toggled += (_, _) =>
        {
            ShowState();
            if (!_updating)
            {
                Write(key, toggle.IsOn);
            }
        };
        Track(() =>
        {
            toggle.IsOn = App.Settings.Get(key) is true;
            ShowState();
        });
    }

    /// <summary>
    /// A segmented control or a combo box over a text setting. With <paramref name="allowCustom"/> a stored value that is not in
    /// the list (a language or device typed into the settings file) is added as its own entry instead of showing nothing.
    /// </summary>
    public void Choose(
        SettingsRow row,
        Selector selector,
        string key,
        IReadOnlyList<Choice> choices,
        string titleKey,
        string? descriptionKey = null,
        bool allowCustom = false)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(selector, row.Header);
        List<Choice> items = [.. choices];
        foreach (Choice choice in items)
        {
            selector.Items.Add(choice.Label);
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (!_updating && selector.SelectedIndex >= 0)
            {
                Write(key, items[selector.SelectedIndex].Value);
            }
        };
        Track(() =>
        {
            string value = App.Settings.Get(key) as string ?? "";
            int index = items.FindIndex(c => c.Value == value);
            if (index < 0 && allowCustom)
            {
                items.Add(new Choice(value, value));
                selector.Items.Add(value);
                index = items.Count - 1;
            }
            selector.SelectedIndex = index;
        });
    }

    /// <summary>A slider with its value beside it: signed by default (+6, 0, -12), or as <paramref name="format"/> shows it.</summary>
    public void Slider(SettingsRow row, Slider slider, TextBlock valueText, string key, string titleKey, string? descriptionKey = null, Func<int, string>? format = null)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(slider, row.Header);
        slider.ValueChanged += (_, e) =>
        {
            if (!_updating)
            {
                Write(key, (int)Math.Round(e.NewValue));
            }
        };
        Track(() =>
        {
            int value = App.Settings.Get(key) is int stored ? stored : 0;
            bool changed = !App.Settings.IsDefault(key);
            slider.Value = value;
            valueText.Text = format?.Invoke(value) ?? (value > 0 ? "+" + value.ToString(CultureInfo.CurrentCulture) : value.ToString(CultureInfo.CurrentCulture));
            AutomationProperties.SetHelpText(slider, changed ? AppStrings.Get("Settings_ChangedFromDefault") : "");
        });
    }

    /// <summary>A one-line text box over a text setting; the value is saved when the box loses focus or Enter is pressed.</summary>
    public void TextEntry(SettingsRow row, TextBox box, string key, string titleKey, string? descriptionKey = null)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(box, row.Header);
        void Commit()
        {
            if (!_updating)
            {
                Write(key, box.Text);
            }
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                Commit();
            }
        };
        Track(() =>
        {
            string stored = App.Settings.Get(key) as string ?? "";
            if (box.Text != stored && box.FocusState == FocusState.Unfocused)
            {
                box.Text = stored;
            }
        });
    }

    /// <summary>A button that puts every one of the settings back to its default; it is off while they all are.</summary>
    public void ResetButton(Button reset, string label, params string[] keys)
    {
        AutomationProperties.SetName(reset, label);
        reset.Click += (_, _) =>
        {
            foreach (string key in keys)
            {
                App.Settings.Reset(key);
            }
            SettingsSaver.Save();
        };
        Track(() => reset.IsEnabled = !keys.All(App.Settings.IsDefault));
    }

    /// <summary>
    /// While the setting differs from its default the row says so in place of its description, and a Reset button
    /// beside the control puts it back.
    /// </summary>
    public void ChangedHint(SettingsRow row, Button reset, string key)
    {
        string? description = row.Description;
        reset.Content = AppStrings.Get("Settings_Reset");
        AutomationProperties.SetName(reset, $"{AppStrings.Get("Settings_ResetToDefault")}: {row.Header}");
        reset.Click += (_, _) =>
        {
            App.Settings.Reset(key);
            SettingsSaver.Save();
        };
        Track(() =>
        {
            bool changed = !App.Settings.IsDefault(key);
            row.Description = changed ? AppStrings.Get("Settings_ChangedFromDefault") : description;
            reset.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    /// <summary>
    /// Shows "Applies on restart · Restart engine" while any of the settings differs from the value the engine started
    /// with. The link restarts the engine in place with the new values; the open file carries on where it was.
    /// </summary>
    public void RestartNote(TextBlock note, params string[] keys)
    {
        var link = new Hyperlink
        {
            UnderlineStyle = UnderlineStyle.Single,
            Foreground = SettingsGroup.Resource<Microsoft.UI.Xaml.Media.Brush>("AetherTextPrimaryBrush"),
        };
        link.Inlines.Add(new Run { Text = AppStrings.Get("Settings_RestartEngine") });
        link.Click += (_, _) => App.RestartEngine();
        note.Inlines.Clear();
        note.Inlines.Add(new Run { Text = AppStrings.Get("Settings_AppliesOnRestart") + " · " });
        note.Inlines.Add(link);
        note.ActualThemeChanged += (_, _) => link.Foreground = SettingsGroup.Resource<Microsoft.UI.Xaml.Media.Brush>("AetherTextPrimaryBrush");
        Track(() => note.Visibility = keys.Any(App.Settings.IsRestartPending) ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void Write<T>(string key, T value)
    {
        if (App.Settings.Set(key, value))
        {
            SettingsSaver.Save();
        }
    }

    private void Run(Action refresh)
    {
        bool was = _updating;
        _updating = true;
        try
        {
            refresh();
        }
        finally
        {
            _updating = was;
        }
    }

    private void RefreshAll()
    {
        foreach (Action refresh in _refreshers)
        {
            Run(refresh);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        App.Settings.Changed -= OnChanged;
        App.Settings.Changed += OnChanged;
        App.Settings.Started -= OnStarted;
        App.Settings.Started += OnStarted;
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.Settings.Changed -= OnChanged;
        App.Settings.Started -= OnStarted;
    }

    private void OnStarted(object? sender, EventArgs e) => OnChanged(sender, null);

    // Settings can change on a background thread (the first load, a reset); the controls are only touched on the UI thread.
    private void OnChanged(object? sender, SettingChangedEventArgs? e)
    {
        if (_page.DispatcherQueue.HasThreadAccess)
        {
            RefreshAll();
        }
        else
        {
            _page.DispatcherQueue.TryEnqueue(RefreshAll);
        }
    }
}
