using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Aether_App;

/// <summary>
/// Shows toasts on the page: feeds requests to the <see cref="ToastQueue"/>, draws what it says is showing in the
/// overlay <see cref="Toast"/>, and arms the timer it asks for. Any code that wants a toast calls <see cref="Show(ToastRequest)"/>.
/// UI thread only.
/// </summary>
internal sealed class ToastService
{
    private const double AboveControlsMargin = 116; // 8 above the 84 px pill, which sits 24 up
    private const double BottomMargin = 24;

    private readonly ToastQueue _queue;
    private readonly Toast _toast;
    private readonly FrameworkElement _layer;
    private readonly ChromeFader _fader;
    private readonly Action _returnFocus;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _hideTimer;
    private bool _focused;

    /// <param name="layer">The overlay holding <paramref name="toast"/>: bottom aligned, with 16 side margins.</param>
    /// <param name="returnFocus">Puts keyboard focus back on the page when a toast that had it goes away.</param>
    public ToastService(
        Toast toast, FrameworkElement layer, ChromeFader fader, DispatcherQueue dispatcher, Action returnFocus, TimeProvider? time = null)
    {
        _toast = toast;
        _layer = layer;
        _fader = fader;
        _returnFocus = returnFocus;
        _queue = new ToastQueue(time);
        _queue.Changed += OnQueueChanged;

        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) =>
        {
            _queue.Tick();
            Arm();
        };
        _hideTimer = dispatcher.CreateTimer();
        _hideTimer.IsRepeating = false;
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            _layer.Visibility = Visibility.Collapsed;
        };

        toast.HoverChanged += (_, hovered) =>
        {
            _queue.SetHovered(hovered);
            Arm();
        };
        toast.FocusChanged += (_, focused) =>
        {
            _focused = focused;
            _queue.SetFocused(focused);
            Arm();
        };
        toast.ActionInvoked += (_, _) => Close();
        toast.DismissRequested += (_, _) => Close();

        _layer.Visibility = Visibility.Collapsed;
        _fader.Fade(_layer, false);
    }

    public void Show(string key, string text, ToastTone tone = ToastTone.Neutral, string? actionLabel = null, Action? action = null) =>
        Show(new ToastRequest(key, text, tone, actionLabel, action));

    public void Show(ToastRequest request)
    {
        _queue.Show(request);
        Arm();
    }

    /// <summary>Toasts wait, with their timer held, while a dialog or flyout is open.</summary>
    public void SetBlocked(bool blocked)
    {
        _queue.SetBlocked(blocked);
        Arm();
    }

    /// <summary>Sits 8 above the pill while the controls show, 24 from the bottom once they hide.</summary>
    public void SetControlsShown(bool shown)
    {
        Thickness margin = _layer.Margin;
        _layer.Margin = new Thickness(margin.Left, margin.Top, margin.Right, shown ? AboveControlsMargin : BottomMargin);
    }

    private void Close()
    {
        bool hadFocus = _focused;
        _queue.Dismiss();
        Arm();
        if (hadFocus)
        {
            _returnFocus();
        }
    }

    private void OnQueueChanged(object? sender, EventArgs e)
    {
        if (_queue.Showing is { } request)
        {
            _hideTimer.Stop();
            _toast.Present(request);
            _layer.Visibility = Visibility.Visible;
            _fader.Fade(_layer, true);
        }
        else
        {
            _focused = false;
            _fader.Fade(_layer, false);
            // Collapsed once the fade is done, so the invisible toast takes no clicks and is left out of the tab order.
            double fade = _fader.DurationMilliseconds(false);
            _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(fade, 1));
            _hideTimer.Start();
        }
    }

    private void Arm()
    {
        if (_queue.DueIn is { } due)
        {
            _timer.Interval = due > TimeSpan.Zero ? due : TimeSpan.FromMilliseconds(1);
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }
}
