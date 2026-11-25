using Aether.Core.Playback;
using Aether_App.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Aether_App;

/// <summary>
/// Shows toasts on the page: feeds requests to the <see cref="ToastQueue"/>, draws what it says is showing in the
/// overlay <see cref="Toast"/>, and arms the timer it asks for. Any code that wants a toast calls <see cref="Show(ToastRequest)"/>.
/// Defers realizing the toast visual tree until the first toast is shown. UI thread only.
/// </summary>
internal sealed class ToastService
{
    private const double BottomMargin = 24; // the card's own margin too
    private const double Gap = 8; // above the card, and above the chip

    private readonly ToastQueue _queue;
    private readonly Func<(Toast Card, FrameworkElement Layer)> _ensureView;
    private readonly ChromeFader _fader;
    private readonly Action _returnFocus;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _hideTimer;
    private Toast? _toast;
    private FrameworkElement? _layer;
    private bool _focused;
    private bool _shown;
    private bool _controlsShown;
    private double _controlsHeight;
    private double _chipHeight;
    private bool _hasPlacement;

    /// <param name="ensureView">Realizes the overlay holding <see cref="Toast"/> on first use.</param>
    /// <param name="returnFocus">Puts keyboard focus back on the page when a toast that had it goes away.</param>
    public ToastService(
        Func<(Toast Card, FrameworkElement Layer)> ensureView,
        ChromeFader fader,
        DispatcherQueue dispatcher,
        Action returnFocus,
        TimeProvider? time = null)
    {
        _ensureView = ensureView;
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
            if (_layer is not null)
            {
                _layer.Visibility = Visibility.Collapsed;
            }
            Collapsed?.Invoke();
        };
    }

    /// <summary>True while a toast is actively shown (false once dismissed or fading out).</summary>
    public bool IsShown => _shown;

    /// <summary>Raised when the toast begins fading in or out, with the matching fade duration in ms.</summary>
    public event Action<bool, double>? VisibilityChanged;

    /// <summary>Raised once the exit fade completes and the toast layer is collapsed.</summary>
    public event Action? Collapsed;

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

    /// <summary>
    /// Sits 8 above the card while the controls show, 24 from the bottom once they hide. <paramref name="controlsHeight"/>
    /// is the card as it is drawn now (slim, full or growing); <paramref name="chipHeight"/> is the export chip or clip
    /// drawer between the two, which the toast clears by 8.
    /// </summary>
    public void SetControlsShown(bool shown, double controlsHeight, double chipHeight = 0)
    {
        _controlsShown = shown;
        _controlsHeight = controlsHeight;
        _chipHeight = chipHeight;
        _hasPlacement = true;
        if (_layer is not null)
        {
            ApplyPlacement(_layer, shown, controlsHeight, chipHeight);
        }
    }

    private static void ApplyPlacement(FrameworkElement layer, bool shown, double controlsHeight, double chipHeight)
    {
        Thickness margin = layer.Margin;
        double bottom = shown ? BottomMargin + controlsHeight + Gap + (chipHeight > 0 ? chipHeight + Gap : 0) : BottomMargin;
        layer.Margin = new Thickness(margin.Left, margin.Top, margin.Right, bottom);
    }

    private void EnsureToastView()
    {
        if (_toast is not null && _layer is not null)
        {
            return;
        }
        (Toast toast, FrameworkElement layer) = _ensureView();
        _toast = toast;
        _layer = layer;
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
        layer.Visibility = Visibility.Collapsed;
        _fader.Reveal(layer, false);
        if (_hasPlacement)
        {
            ApplyPlacement(layer, _controlsShown, _controlsHeight, _chipHeight);
        }
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
            EnsureToastView();
            _hideTimer.Stop();
            _toast!.Present(request);
            _layer!.Visibility = Visibility.Visible;
            _fader.Reveal(_layer, true);
            if (!_shown)
            {
                _shown = true;
                _fader.Slide(_toast, true); // a message replacing one on screen stays put
                VisibilityChanged?.Invoke(true, _fader.RevealDurationMilliseconds(true));
            }
        }
        else if (_toast is not null && _layer is not null)
        {
            _focused = false;
            _shown = false;
            _fader.Reveal(_layer, false);
            _fader.Slide(_toast, false);
            double fade = _fader.RevealDurationMilliseconds(false);
            VisibilityChanged?.Invoke(false, fade);
            // Collapsed once the fade is done, so the invisible toast takes no clicks and is left out of the tab order.
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
