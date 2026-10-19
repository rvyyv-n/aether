using Aether.Core.Playback;
using Aether.Core.Playback.Chapters;
using Aether.PvP.Review.Clipping;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Aether_App.Controls;

/// <summary>
/// The transport's seek bar (screen 5, frames 5.1–5.3): hover shows the thumb, a pointer line and a time
/// bubble; dragging grows the thumb and reports keyframe seeks, and the release reports one exact seek.
/// The bar also draws the buffered range, the In/Out band and a gap at each chapter start, and names the chapter
/// in the bubble. UI thread only.
/// </summary>
public sealed partial class PrecisionSeekBar : UserControl
{
    private const double ThumbSize = 12;
    private const double DragThumbSize = 16;
    private const double WindowInset = 8; // the bubble keeps clear of the window edges
    private const double ChapterGap = 2;
    private const double MinChapterPiece = 6; // closer chapter starts share a piece, so the bar never turns to slivers

    private double _duration;
    private double _position;
    private double _bufferedUntil = double.NaN;
    private ClipRange _range;
    private double? _cutStart;
    private bool _hovering;
    private double _pointerX = double.NaN;
    private double _dragTime;
    private ChapterList _chapters = ChapterList.Empty;
    private readonly List<Piece> _pieces = [];

    public PrecisionSeekBar()
    {
        InitializeComponent();
        ActualThemeChanged += (_, _) => ApplyBrushes();
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

    /// <summary>The open file's chapters; <see cref="ChapterList.Empty"/> clears the marks.</summary>
    public ChapterList Chapters
    {
        get => _chapters;
        set
        {
            if (value != _chapters)
            {
                _chapters = value;
                Arrange();
            }
        }
    }

    private double BarWidth => Root.ActualWidth;

    private double X(double seconds) => SeekBarMath.OffsetOf(seconds, BarWidth, _duration);

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        Arrange();
    }

    private void Arrange()
    {
        ArrangePieces();
        ArrangeProgress();
        ArrangeBuffered();
        ArrangeRange();
        ArrangeHover();
    }

    private void ArrangeProgress()
    {
        double x = X(IsDragging ? _dragTime : _position);
        foreach (Piece piece in _pieces)
        {
            piece.Played.Width = piece.Fill(x);
        }
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
        double x = show ? X(_bufferedUntil) : 0;
        foreach (Piece piece in _pieces)
        {
            piece.Buffered.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            piece.Buffered.Width = piece.Fill(x);
        }
    }

    /// <summary>Splits the track into one piece per chapter, reusing the pieces already drawn.</summary>
    private void ArrangePieces()
    {
        double width = BarWidth;
        var segments = SeekBarMath.ChapterSegments(_chapters.Fractions(_duration), width, ChapterGap, MinChapterPiece);
        while (_pieces.Count < segments.Count)
        {
            var piece = new Piece(
                Bar("AetherFillProgressTrackBrush"), Bar("AetherFillRangeBrush"), Bar("AetherTextPrimaryBrush"));
            Pieces.Children.Add(piece.Track);
            Pieces.Children.Add(piece.Buffered);
            Pieces.Children.Add(piece.Played);
            _pieces.Add(piece);
        }
        while (_pieces.Count > segments.Count)
        {
            Piece piece = _pieces[^1];
            Pieces.Children.Remove(piece.Track);
            Pieces.Children.Remove(piece.Buffered);
            Pieces.Children.Remove(piece.Played);
            _pieces.RemoveAt(_pieces.Count - 1);
        }
        for (int i = 0; i < segments.Count; i++)
        {
            Piece piece = _pieces[i];
            (piece.Start, piece.End) = segments[i];
            piece.Track.Width = piece.End - piece.Start;
            Canvas.SetLeft(piece.Track, piece.Start);
            Canvas.SetLeft(piece.Buffered, piece.Start);
            Canvas.SetLeft(piece.Played, piece.Start);
        }
    }

    // The brush key rides in Tag so the strips can be recoloured when the theme changes.
    private Border Bar(string brushKey) => new()
    {
        Height = 4,
        CornerRadius = new CornerRadius(2),
        Tag = brushKey,
        Background = ThemeTokens.Get<Brush>(this, brushKey),
    };

    private void ApplyBrushes()
    {
        foreach (Piece piece in _pieces)
        {
            foreach (Border strip in new[] { piece.Track, piece.Buffered, piece.Played })
            {
                strip.Background = ThemeTokens.Get<Brush>(this, (string)strip.Tag);
            }
        }
    }

    /// <summary>
    /// Where a Fast cut really starts (the keyframe at or before In). Null, or a keyframe within about
    /// one frame of In, hides the lead-in.
    /// </summary>
    public void SetCutStart(double? seconds)
    {
        if (seconds != _cutStart)
        {
            _cutStart = seconds;
            ArrangeRange();
            double? lead = _range.In is { } at ? CutStart.Lead(at, seconds) : null;
            AutomationProperties.SetHelpText(this, lead is { } early
                ? string.Format(CultureInfo.CurrentCulture, AppStrings.Get("Seek_CutStartHelp"), CutStart.Format(early))
                : string.Empty);
        }
    }

    private void ArrangeRange()
    {
        bool live = _duration > 0;
        bool lead = live && _range.In is { } at && CutStart.Lead(at, _cutStart) is not null;
        LeadIn.Visibility = lead ? Visibility.Visible : Visibility.Collapsed;
        PlaceTick(KeyTick, lead ? _cutStart : null);
        if (lead)
        {
            double from = X(_cutStart!.Value);
            Canvas.SetLeft(LeadIn, from);
            LeadIn.Width = Math.Max(0, X(_range.In!.Value) - from);
        }
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
        int chapter = _chapters.IndexAt(seconds);
        string title = chapter >= 0 ? _chapters[chapter].Title.Trim() : "";
        ChapterText.Text = title;
        ChapterText.Visibility = title.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
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

    /// <summary>One chapter's stretch of the track, with its own buffered and played fill.</summary>
    private sealed class Piece(Border track, Border buffered, Border played)
    {
        public Border Track { get; } = track;
        public Border Buffered { get; } = buffered;
        public Border Played { get; } = played;
        public double Start { get; set; }
        public double End { get; set; }

        /// <summary>How much of this piece a fill reaching <paramref name="x"/> covers.</summary>
        public double Fill(double x) => Math.Clamp(x - Start, 0, End - Start);
    }
}
