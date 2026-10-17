using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;
using Aether_App.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Aether_App;

/// <summary>
/// The quality picker for links (9.8): a pill button beside Speed, shown only while a link plays, listing the sizes
/// yt-dlp reports for it. A pick is remembered for later links and switches the playing one in place: the link is
/// resolved again for that size while the old stream keeps playing, then the new stream opens where playback had got
/// to. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private IReadOnlyList<QualityOption> _qualityOptions = [];
    private QualityOption _quality = QualityOptions.Auto;
    private CancellationTokenSource? _qualitySwitchCts;
    private bool _qualitySwitching; // the switched stream is loading: the picture's place is kept, not the home screen's
    private bool _qualityMenuOpen;

    /// <summary>The selector a link's first resolve asks for: the remembered size, or null for the Streaming quality setting.</summary>
    private static string? RememberedQualitySelector()
    {
        AetherSettings settings = App.Settings.Current;
        return QualityOptions.FirstResolveSelector(
            settings.StreamQualityChoice, MpvSettingsMap.YtdlFormat(settings.StreamingQuality, settings.PreferGpuDecodableFormats));
    }

    private static StreamResolutionOptions StreamOptions(string? formatSelector)
    {
        AetherSettings settings = App.Settings.Current;
        return new StreamResolutionOptions(
            YtDlpPath: ExternalTools.FindYtDlp(),
            StreamingQuality: settings.StreamingQuality,
            PreferGpuDecodableFormats: settings.PreferGpuDecodableFormats,
            HttpProxy: string.IsNullOrWhiteSpace(settings.HttpProxy) ? null : settings.HttpProxy,
            Timeout: TimeSpan.FromSeconds(30),
            FormatSelectorOverride: formatSelector);
    }

    /// <summary>A new file or link replaces the list, and any switch still resolving for the last one.</summary>
    private void ResetQuality(OpenedLink? link)
    {
        CancelQualitySwitch();
        _qualitySwitching = false;
        _qualityOptions = link?.Qualities ?? [];
        QualityButton.Visibility = link is null ? Visibility.Collapsed : Visibility.Visible;
        ShowQuality(link?.Quality ?? QualityOptions.Auto);
    }

    private void CancelQualitySwitch()
    {
        _qualitySwitchCts?.Cancel();
        _qualitySwitchCts = null;
    }

    private static string QualityLabel(QualityOption option) =>
        option.IsAuto ? AppStrings.Get("Transport_QualityAuto")
        : option.AudioOnly ? AppStrings.Get("Transport_QualityAudioOnly")
        : option.Label;

    private void ShowQuality(QualityOption option)
    {
        _quality = option;
        string label = QualityLabel(option);
        QualityButton.Content = label;
        AutomationProperties.SetName(QualityButton, $"{AppStrings.Get("Transport_Quality")} {label}");
        foreach (MenuFlyoutItemBase item in QualityMenu.Items)
        {
            if (item is ToggleMenuFlyoutItem { Tag: QualityOption row } toggle)
            {
                toggle.IsChecked = row.Key == option.Key;
            }
        }
    }

    // The rows are built as the menu opens: each link lists its own sizes.
    private void OnQualityClick(object sender, RoutedEventArgs e)
    {
        QualityMenu.Items.Clear();
        foreach (QualityOption option in _qualityOptions.Count > 0 ? _qualityOptions : [QualityOptions.Auto])
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = QualityLabel(option),
                Tag = option,
                IsChecked = option.Key == _quality.Key,
                Style = (Style)Application.Current.Resources["AetherToggleMenuItemStyle"],
            };
            item.Click += OnQualityItemClick;
            QualityMenu.Items.Add(item);
        }
        FlyoutBase.ShowAttachedFlyout(QualityButton);
    }

    private void OnQualityItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleMenuFlyoutItem { Tag: QualityOption option })
        {
            SwitchQuality(option);
        }
    }

    private void OnQualityMenuOpened(object sender, object e)
    {
        _qualityMenuOpen = true;
        ApplyQualityLook();
        HoldForMenu(true);
    }

    private void OnQualityMenuClosed(object sender, object e)
    {
        _qualityMenuOpen = false;
        ApplyQualityLook();
        HoldForMenu(false);
    }

    // Filled while its menu is open, as Speed is.
    private void ApplyQualityLook()
    {
        if (_qualityMenuOpen)
        {
            QualityButton.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AetherFillEmphasisRestBrush"];
        }
        else
        {
            QualityButton.ClearValue(BackgroundProperty);
        }
    }

    private static void RememberQuality(QualityOption option)
    {
        string key = option.IsAuto ? "" : option.Key;
        if (App.Settings.Current.StreamQualityChoice == key || !App.Settings.Set(SettingKeys.StreamQualityChoice, key))
        {
            return;
        }
        _ = Task.Run(() =>
        {
            try
            {
                App.Settings.Save();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Remembered for this session at worst.
            }
        });
    }

    /// <summary>
    /// Keeps the old stream playing while the link resolves for the new size, then opens the new stream at the position
    /// playback has reached by then, with its pause, tracks and delays; volume, speed and loop stay on the same engine.
    /// A later pick cancels an earlier one still resolving. On failure the old stream plays on under a toast.
    /// </summary>
    private async void SwitchQuality(QualityOption option)
    {
        RememberQuality(option);
        if (_currentWebLink is not { } link || _session is not { } session || option.Key == _quality.Key)
        {
            ShowQuality(_quality); // a click on the checked row would otherwise uncheck it
            return;
        }

        CancelQualitySwitch();
        var cts = new CancellationTokenSource();
        _qualitySwitchCts = cts;
        QualityOption previous = _quality;
        ShowQuality(option); // at once, so the menu and a second pick follow what was asked for

        try
        {
            ResolvedStream resolved = await _streamResolver.ResolveAsync(link, StreamOptions(option.FormatSelector()), cts.Token);
            if (cts.IsCancellationRequested || !_isLoaded || _session != session || _currentWebLink != link)
            {
                return;
            }

            PlayerViewModel vm = session.ViewModel;
            bool playing = HasVideo;
            var carry = new PlaybackCarryOver(
                _currentPath, playing ? vm.Position : null, playing && vm.IsPaused, vm.Volume, vm.Speed, vm.IsLooping, _trackChoices, link);
            IReadOnlyList<QualityOption> qualities = QualityOptions.From(resolved.Formats);
            string title = _streamTitle ?? WebLink.Host(link) ?? link;
            Open(resolved.StreamUrl, carry, new OpenedLink(
                link, title, resolved.HttpHeaders, resolved.AudioUrl, resolved.Chapters, resolved.Subtitles,
                qualities, QualityOptions.Resolve(option.Key, qualities)));
            _qualitySwitching = playing;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Replaced by a later pick, or by another file or link.
        }
        catch (Exception)
        {
            if (_isLoaded && _currentWebLink == link && _qualitySwitchCts == cts)
            {
                ShowQuality(previous);
                _toasts?.Show("quality-switch-failed", AppStrings.Get("Toast_QualitySwitchFailed"));
            }
        }
        finally
        {
            if (_qualitySwitchCts == cts)
            {
                _qualitySwitchCts = null;
            }
        }
    }

    /// <summary>The switched stream has loaded, or failed to: the page shows what is true again.</summary>
    private void EndQualitySwitchOnceLoaded()
    {
        if (_qualitySwitching && _session?.ViewModel.Status != PlaybackStatus.Loading)
        {
            _qualitySwitching = false;
        }
    }
}
