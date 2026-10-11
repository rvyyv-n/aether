using Aether.Core.Playback;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Aether_App.Controls;

/// <summary>
/// The transport's seek bar (screen 5, frames 5.1–5.3): hover shows the thumb, a pointer line and a time
/// bubble; dragging grows the thumb and reports keyframe seeks, and the release reports one exact seek.
/// The bar also draws the buffered range and the In/Out band. UI thread only.
/// </summary>
public sealed partial class PrecisionSeekBar : UserControl
{
    private const double ThumbSize = 12;
    private const double DragThumbSize = 16;
    private const double WindowInset = 8; // the bubble keeps clear of the window edges

    private double _duration;
    private double _position;
    private double _bufferedUntil = double.NaN;
    private ClipRange _range;
    private bool _hovering;
    private double _pointerX = double.NaN;
    private double _dragTime;

    public PrecisionSeekBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// A seek the user asked for: <c>dragging</c> while the thumb follows the pointer (keyframe seek),
    /// then once more on release with <c>dragging</c> false (exact seek).
    /// </summary>
    public event Action<double, bool>? SeekRequested;

    /// <summary>The thumb is held: the bar and the clock follow the pointer, not playback.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>The time under the thumb while dragging.</summary>
    public double DragTime => _dragTime;

    /// <summary>Distance from the bar's top to the bubble's bottom: 8 to the pill's top plus 16 above it.</summary>
    public double BubbleLift { get; set; } = 24;

    public double Duration
    {
        get => _duration;
        set
        {
            double duration = double.IsFinite(value) && value > 0 ? value : 0;
            if (duration != _duration)
            {
                _duration = duration;
                Arrange();
            }
        }
    }

    public double Position
    {
        get => _position;
        set
        {
            if (value != _position)
            {
                _position = value;
                if (!IsDragging)
                {
                    ArrangeProgress();
                }
            }
        }
    }

    /// <summary>End of the buffered range in seconds, or NaN when nothing is buffered ahead (local files).</summary>
    public double BufferedUntil
    {
        get => _bufferedUntil;
        set
        {
            if (!value.Equals(_bufferedUntil))
            {
                _bufferedUntil = value;
                ArrangeBuffered();
            }
        }
    }

    public ClipRange Range
    {
        get => _range;
        set
        {
            if (value != _range)
            {
                _range = value;
                ArrangeRange();
            }
        }
    }

    private double BarWidth => Root.ActualWidth;

    private double X(double seconds) => SeekBarMath.OffsetOf(seconds, BarWidth, _duration);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Track.Width = e.NewSize.Width;
        Arrange();
    }

    private void Arrange()
    {
        ArrangeProgress();
        ArrangeBuffered();
        ArrangeRange();
        ArrangeHover();
    }

    private void ArrangeProgress()
    {
        double x = X(IsDragging ? _dragTime : _position);
        Played.Width = x;
        double size = IsDragging ? DragThumbSize : ThumbSize;
        Thumb.Width = size;
        Thumb.Height = size;
        Canvas.SetLeft(Thumb, x - size / 2);
        Canvas.SetTop(Thumb, (Root.Height - size) / 2);
        Thumb.Visibility = (_hovering || IsDragging) && _duration > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ArrangeBuffered()
    {
        bool show = _duration > 0 && double.IsFinite(_bufferedUntil) && _bufferedUntil > 0;
        Buffered.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show)
        {
            Buffered.Width = X(_bufferedUntil);
        }
    }

    private void ArrangeRange()
    {
        bool live = _duration > 0;
        PlaceTick(InTick, live ? _range.In : null);
        PlaceTick(OutTick, live ? _range.Out : null);
        bool band = live && _range.IsComplete;
        Band.Visibility = band ? Visibility.Visible : Visibility.Collapsed;
        if (band)
        {
            double start = X(_range.In!.Value);
            Canvas.SetLeft(Band, start);
            Band.Width = Math.Max(0, X(_range.Out!.Value) - start);
        }
    }

    private void PlaceTick(FrameworkElement tick, double? at)
    {
        tick.Visibility = at is null ? Visibility.Collapsed : Visibility.Visible;
        if (at is { } seconds)
        {
            Canvas.SetLeft(tick, X(seconds) - tick.Width / 2);
        }
    }

    private void ArrangeHover()
    {
        bool line = _hovering && !IsDragging && _duration > 0 && double.IsFinite(_pointerX);
        HoverLine.Visibility = line ? Visibility.Visible : Visibility.Collapsed;
        if (line)
        {
            Canvas.SetLeft(HoverLine, Math.Clamp(_pointerX, 0, BarWidth) - 0.5);
        }
        ShowBubble();
    }

    private void ShowBubble()
    {
        bool show = (_hovering || IsDragging) && _duration > 0 && double.IsFinite(_pointerX) && XamlRoot is not null;
        if (!show)
        {
            BubblePopup.IsOpen = false;
            return;
        }
        double seconds = IsDragging ? _dragTime : SeekBarMath.TimeAt(_pointerX, BarWidth, _duration);
        BubbleText.Text = Timecode.FormatClock(seconds, _duration);
        BubblePopup.IsOpen = true;
        Bubble.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size size = Bubble.DesiredSize;

        double barLeft = Root.TransformToVisual(null).TransformPoint(default).X;
        double windowWidth = XamlRoot!.Size.Width;
        double centre = barLeft + X(seconds);
        BubblePopup.HorizontalOffset = SeekBarMath.CentreWithin(centre, size.Width, WindowInset, windowWidth - WindowInset) - barLeft;
        BubblePopup.VerticalOffset = -BubbleLift - size.Height;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _hovering = true;
        _pointerX = e.GetCurrentPoint(Root).Position.X;
        ArrangeProgress();
        ArrangeHover();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        _pointerX = e.GetCurrentPoint(Root).Position.X;
        if (IsDragging)
        {
            DragTo(_pointerX);
        }
        else
        {
            _hovering = true;
            ArrangeProgress();
            ArrangeHover();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _hovering = false;
        if (!IsDragging)
        {
            ArrangeProgress();
            ArrangeHover();
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Root);
        if (_duration <= 0 || !point.Properties.IsLeftButtonPressed || !Root.CapturePointer(e.Pointer))
        {
            return;
        }
        e.Handled = true;
        IsDragging = true;
        _pointerX = point.Position.X;
        DragTo(_pointerX);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (IsDragging)
        {
            e.Handled = true;
            Root.ReleasePointerCapture(e.Pointer); // raises PointerCaptureLost, which ends the drag
        }
    }

    /// <summary>Release, a lost capture or a cancelled pointer: one exact seek to where the thumb stopped.</summary>
    private void OnPointerCaptureEnded(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging)
        {
            return;
        }
        IsDragging = false;
        _position = _dragTime; // hold the thumb here until playback reports the new position
        _hovering = IsPointerOver(e);
        ArrangeProgress();
        ArrangeHover();
        SeekRequested?.Invoke(_dragTime, false);
    }

    private void DragTo(double x)
    {
        _dragTime = SeekBarMath.TimeAt(x, BarWidth, _duration);
        ArrangeProgress();
        ArrangeHover();
        SeekRequested?.Invoke(_dragTime, true);
    }

    private bool IsPointerOver(PointerRoutedEventArgs e)
    {
        Point p = e.GetCurrentPoint(Root).Position;
        return p.X >= 0 && p.Y >= 0 && p.X <= Root.ActualWidth && p.Y <= Root.ActualHeight;
    }
}
