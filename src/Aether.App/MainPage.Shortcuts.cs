using Aether.Core.Playback;
using Windows.ApplicationModel.DataTransfer;
using Aether.Core.Playback.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Aether_App;

/// <summary>
/// Builds the page's keyboard accelerators from the shortcut settings and keeps the key hints in tooltips and the
/// More menu in step with them. Escape and the frame-step keys are fixed and live elsewhere. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private readonly List<KeyboardAccelerator> _shortcutAccelerators = [];

    /// <summary>Replaces the configurable accelerators with the current bindings and refreshes the key hints.</summary>
    private void BuildShortcuts()
    {
        foreach (KeyboardAccelerator accelerator in _shortcutAccelerators)
        {
            KeyboardAccelerators.Remove(accelerator);
        }
        _shortcutAccelerators.Clear();

        ShortcutMap map = App.Shortcuts.Map;
        var taken = new HashSet<KeyCombination>();
        foreach (ShortcutAction action in ShortcutActions.All)
        {
            foreach (KeyCombination combination in map.Get(action))
            {
                // Two actions on one combination: the first in the list wins, as ShortcutMap.Find says.
                if (!taken.Add(combination))
                {
                    continue;
                }
                var accelerator = new KeyboardAccelerator
                {
                    Key = (Windows.System.VirtualKey)combination.Key,
                    Modifiers = ToModifiers(combination.Modifiers),
                };
                accelerator.Invoked += HandlerFor(action);
                accelerator.IsEnabled = !_tour.IsActive;
                KeyboardAccelerators.Add(accelerator);
                _shortcutAccelerators.Add(accelerator);
            }
        }

        var pasteAccelerator = new KeyboardAccelerator
        {
            Key = Windows.System.VirtualKey.V,
            Modifiers = Windows.System.VirtualKeyModifiers.Control,
        };
        pasteAccelerator.Invoked += OnPasteAccelerator;
        pasteAccelerator.IsEnabled = !_tour.IsActive;
        KeyboardAccelerators.Add(pasteAccelerator);
        _shortcutAccelerators.Add(pasteAccelerator);
    }

    // The settings change on the thread pool when they load, so the page is updated on its own thread.
    private void OnShortcutsChanged(object? sender, EventArgs e) => DispatcherQueue.TryEnqueue(() =>
    {
        if (!_isLoaded)
        {
            return;
        }
        BuildShortcuts();
        ApplyShortcutLabels();
        SyncVolume();
        Render();
    });

    /// <summary>The tooltips and menu hints that name a configurable key.</summary>
    private void ApplyShortcutLabels()
    {
        ToolTipService.SetToolTip(OpenUrlButton, Tip("EmptyState_OpenUrlTooltip", ShortcutAction.OpenUrl));
        ToolTipService.SetToolTip(OpenButton, Tip("Transport_OpenFile", ShortcutAction.OpenFile));
        Label(BackButton, "Transport_Back5s", ShortcutAction.SkipBack);
        Label(ForwardButton, "Transport_Forward5s", ShortcutAction.SkipForward);
        MoreLoopItem.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.Loop);
        MoreAlwaysOnTopItem.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.AlwaysOnTop);
        MoreSettingsItem.KeyboardAcceleratorTextOverride = App.Shortcuts.Describe(ShortcutAction.Settings);
        ApplyContextMenuShortcuts();
        ApplyRailShortcutLabels();
        ApplyLanStreamShortcutLabels();
    }

    private static string Tip(string key, ShortcutAction action) => App.Shortcuts.Tip(AppStrings.Get(key), action);

    // The home line names the open-file key as it is now, and falls back to a plain prompt when that action has no key.
    private static string HomeFilesSubtitle() =>
        App.Shortcuts.Describe(ShortcutAction.OpenFile) is { Length: > 0 } keys
            ? (keys == "Ctrl+O"
                ? AppStrings.Get("EmptyState_Subtitle")
                : AppStrings.Format("EmptyState_SubtitleWithLink", keys))
            : AppStrings.Get("EmptyState_SubtitleFilesOnlyUnbound");

    private void OnPasteAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CanPasteLink())
        {
            args.Handled = true;
            _ = TryPasteLinkAsync();
        }
    }

    private bool CanPasteLink() =>
        !HasVideo && _openUrl.Stage == OpenUrlStage.Closed && !IsTextInputFocused();

    private async Task TryPasteLinkAsync()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                string text = await content.GetTextAsync();
                if (WebLink.IsValid(text))
                {
                    StartResolvingStream(text.Trim());
                }
            }
        }
        catch (Exception)
        {
            // Clipboard is locked or holds incompatible data
        }
    }

    private TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> HandlerFor(ShortcutAction action) => action switch
    {
        ShortcutAction.OpenFile => OnOpenAccelerator,
        ShortcutAction.OpenUrl => OnOpenUrlAccelerator,
        ShortcutAction.SkipBack => OnBackAccelerator,
        ShortcutAction.SkipForward => OnForwardAccelerator,
        ShortcutAction.SkipBackFine => OnBackFineAccelerator,
        ShortcutAction.SkipForwardFine => OnForwardFineAccelerator,
        ShortcutAction.SkipBackLong => OnBackLongAccelerator,
        ShortcutAction.SkipForwardLong => OnForwardLongAccelerator,
        ShortcutAction.SetIn => OnSetInAccelerator,
        ShortcutAction.SetOut => OnSetOutAccelerator,
        ShortcutAction.PlayPause => OnPlayPauseAccelerator,
        ShortcutAction.FullScreen => OnFullScreenAccelerator,
        ShortcutAction.Mute => OnMuteAccelerator,
        ShortcutAction.Loop => OnLoopAccelerator,
        ShortcutAction.AlwaysOnTop => OnAlwaysOnTopAccelerator,
        ShortcutAction.PictureInPicture => OnPictureInPictureAccelerator,
        ShortcutAction.Diagnostics => OnDiagnosticsAccelerator,
        ShortcutAction.Settings => OnSettingsAccelerator,
        ShortcutAction.CopyFrame => OnCopyFrameAccelerator,
        ShortcutAction.SaveFrame => OnSaveFrameAccelerator,
        ShortcutAction.ExportClip => OnExportClipAccelerator,
        ShortcutAction.NextChapter => OnNextChapterAccelerator,
        ShortcutAction.PreviousChapter => OnPreviousChapterAccelerator,
        ShortcutAction.Sidebar => OnSidebarAccelerator,
        ShortcutAction.ToggleLanStream => OnLanStreamAccelerator,
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    private static Windows.System.VirtualKeyModifiers ToModifiers(KeyModifiers modifiers)
    {
        var result = Windows.System.VirtualKeyModifiers.None;
        if ((modifiers & KeyModifiers.Ctrl) != 0)
        {
            result |= Windows.System.VirtualKeyModifiers.Control;
        }
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            result |= Windows.System.VirtualKeyModifiers.Shift;
        }
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            result |= Windows.System.VirtualKeyModifiers.Menu;
        }
        return result;
    }
}
