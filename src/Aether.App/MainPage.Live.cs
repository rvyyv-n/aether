using Aether.Core.Playback;
using Aether.Core.Playback.Streams;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Aether_App;

/// <summary>
/// Live streams (16.4): yt-dlp says a link is live; then the seek bar spans what mpv holds of it, read once a second
/// from <c>demuxer-cache-state</c>, the clock says how far behind live it is, and a LIVE badge in place of the length
/// pulses at the live edge and jumps back to it when clicked. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private bool _isLive;
    private LiveWindow? _liveWindow;
    private DispatcherQueueTimer? _liveTimer;
    private Storyboard? _livePulse;
    private bool _livePulsing;

    private void InitializeLive()
    {
        _liveTimer = DispatcherQueue.CreateTimer();
        _liveTimer.Interval = TimeSpan.FromSeconds(1);
        _liveTimer.Tick += (_, _) => ReadLiveWindow();
        LiveText.Text = AppStrings.Get("Transport_Live");
        var fade = new DoubleAnimation { From = 1, To = 0.3, Duration = TimeSpan.FromMilliseconds(900), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(fade, LiveDot);
        Storyboard.SetTargetProperty(fade, nameof(Opacity));
        _livePulse = new Storyboard();
        _livePulse.Children.Add(fade);
    }

    private void ResetLive(bool isLive)
    {
        _isLive = isLive;
        _liveWindow = null;
        LiveBadge.Visibility = isLive ? Visibility.Visible : Visibility.Collapsed;
        DurationText.Visibility = isLive ? Visibility.Collapsed : Visibility.Visible;
        if (isLive)
        {
            _liveTimer?.Start();
        }
        else
        {
            _liveTimer?.Stop();
            SetLivePulse(false);
        }
    }

    private async void ReadLiveWindow()
    {
        if (!_isLive || _session is not { } session)
        {
            return;
        }
        string? state = await Task.Run(() =>
        {
            try
            {
                return session.Context.GetPropertyString("demuxer-cache-state");
            }
            catch (Exception e) when (e is Aether.Core.Interop.Mpv.MpvException or ObjectDisposedException)
            {
                return null;
            }
        });
        if (_isLive && _session == session)
        {
            _liveWindow = LiveWindow.Parse(state);
            UpdateClock(session.ViewModel);
        }
    }

    /// <summary>The live part of <see cref="UpdateClock"/>; false for anything that is not live.</summary>
    private bool UpdateLiveClock(PlayerViewModel vm)
    {
        if (!_isLive)
        {
            return false;
        }
        double position = TransportState.DisplayPosition(vm.State);
        if (_liveWindow is not { } window)
        {
            SeekBar.Duration = 0;
            PositionText.Text = Timecode.FormatClock(position, 0);
            ShowLiveEdge(true);
            return true;
        }
        SeekBar.Duration = window.Length;
        SeekBar.Position = window.ToBar(position);
        if (!SeekBar.IsDragging)
        {
            PositionText.Text = Behind(window, position);
        }
        ShowLiveEdge(window.IsAtEdge(position));
        return true;
    }

    /// <summary>"−1:23" behind the edge; nothing at it, where the badge already says LIVE.</summary>
    private static string Behind(LiveWindow window, double position) =>
        window.IsAtEdge(position) ? "" : "−" + Timecode.FormatClock(window.BehindBy(position), window.Length);

    /// <summary>A seek bar time on a live stream is a time inside the window; anything else passes through.</summary>
    private double FromSeekBar(double seconds) => _isLive && _liveWindow is { } window ? window.FromBar(seconds) : seconds;

    private string SeekBarClock(double target, double duration) =>
        _isLive && _liveWindow is { } window ? Behind(window, target) : Timecode.FormatClock(target, duration);

    private void ShowLiveEdge(bool atEdge)
    {
        LiveDot.Fill = ThemeTokens.Get<Brush>(LiveDot, atEdge ? "AetherLiveBrush" : "AetherTextTertiaryBrush");
        string label = AppStrings.Get(atEdge ? "Transport_LiveNow" : "Transport_GoLive");
        ToolTipService.SetToolTip(LiveBadge, label);
        AutomationProperties.SetName(LiveBadge, label);
        SetLivePulse(atEdge && Motion.AnimationsEnabled);
    }

    private void SetLivePulse(bool on)
    {
        if (on == _livePulsing || _livePulse is null)
        {
            return;
        }
        _livePulsing = on;
        if (on)
        {
            _livePulse.Begin();
        }
        else
        {
            _livePulse.Stop();
        }
    }

    private void OnLiveBadgeClick(object sender, RoutedEventArgs e)
    {
        if (_session is { } session && _liveWindow is { } window)
        {
            TrySend(() => session.Seek(window.EdgeTarget, dragging: false));
        }
    }
}
