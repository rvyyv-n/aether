using System.Globalization;
using Aether.Core.Playback.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

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
        toggle.Toggled += (_, _) =>
        {
            if (!_updating)
            {
                Write(key, toggle.IsOn);
            }
        };
        Track(() => toggle.IsOn = App.Settings.Get(key) is true);
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

    /// <summary>A -100..100 slider with its value beside it and a reset button that shows only when it differs from the default.</summary>
    public void Slider(
        SettingsRow row,
        Slider slider,
        TextBlock valueText,
        Button reset,
        string key,
        string titleKey,
        string? descriptionKey = null)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(slider, row.Header);
        string resetLabel = AppStrings.Get("Settings_ResetToDefault");
        AutomationProperties.SetName(reset, resetLabel);
        ToolTipService.SetToolTip(reset, resetLabel);
        slider.ValueChanged += (_, e) =>
        {
            if (!_updating)
            {
                Write(key, (int)Math.Round(e.NewValue));
            }
        };
        reset.Click += (_, _) =>
        {
            App.Settings.Reset(key);
            SettingsSaver.Save();
        };
        Track(() =>
        {
            int value = App.Settings.Get(key) is int stored ? stored : 0;
            bool changed = !App.Settings.IsDefault(key);
            slider.Value = value;
            valueText.Text = value.ToString(CultureInfo.CurrentCulture);
            reset.Visibility = changed ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetHelpText(slider, changed ? AppStrings.Get("Settings_ChangedFromDefault") : "");
        });
    }

    /// <summary>A one-line text setting, written when the box loses focus or Enter is pressed. A blank box restores the default.</summary>
    public void Text(SettingsRow row, TextBox box, string key, string titleKey, string? descriptionKey = null)
    {
        Describe(row, titleKey, descriptionKey);
        AutomationProperties.SetName(box, row.Header);
        void Commit()
        {
            string text = box.Text.Trim();
            if (text.Length == 0)
            {
                App.Settings.Reset(key);
                SettingsSaver.Save();
            }
            else
            {
                Write(key, text);
            }
            Run(() => box.Text = App.Settings.Get(key) as string ?? "");
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                e.Handled = true;
                Commit();
            }
        };
        Track(() =>
        {
            if (!box.FocusState.HasFlag(FocusState.Keyboard) && !box.FocusState.HasFlag(FocusState.Pointer))
            {
                box.Text = App.Settings.Get(key) as string ?? "";
            }
        });
    }

    /// <summary>Shows "Restart Aether to apply" while any of the settings differs from the value the app started with.</summary>
    public void RestartNote(TextBlock note, params string[] keys)
    {
        note.Text = AppStrings.Get("Settings_RestartAetherToApply");
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
        RefreshAll();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => App.Settings.Changed -= OnChanged;

    // Settings can change on a background thread (the first load, a reset); the controls are only touched on the UI thread.
    private void OnChanged(object? sender, SettingChangedEventArgs e)
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
