namespace Aether.Core.Playback.Tour;

/// <summary>A rectangle in the tour layer's coordinates.</summary>
public readonly record struct TourRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public TourRect Inflate(double by) => new(X - by, Y - by, Width + by * 2, Height + by * 2);

    /// <summary>A zero-size rectangle at the centre, where the spotlight shrinks to when a step has none.</summary>
    public TourRect Collapse() => new(CenterX, CenterY, 0, 0);

    /// <summary>
    /// The rectangle <paramref name="t"/> of the way from <paramref name="from"/> to <paramref name="to"/>. A spring
    /// overshoots past 1, so sizes are kept from going negative.
    /// </summary>
    public static TourRect Lerp(TourRect from, TourRect to, double t) => new(
        from.X + (to.X - from.X) * t,
        from.Y + (to.Y - from.Y) * t,
        Math.Max(0, from.Width + (to.Width - from.Width) * t),
        Math.Max(0, from.Height + (to.Height - from.Height) * t));
}

/// <summary>Where the tour's card goes next to what it points at. Pure geometry, so it is unit tested.</summary>
public static class TourLayout
{
    /// <summary>Space between the lit target and the card.</summary>
    public const double Gap = 16;

    /// <summary>Space the card keeps from the window's edges.</summary>
    public const double Margin = 16;

    /// <summary>The right-click menu's width (AetherSizeContextMenu) and where the tour opens it from the left edge.</summary>
    public const double MenuWidth = 276;

    public const double MenuInset = 24;

    /// <summary>
    /// The card's top-left corner in an area of <paramref name="areaWidth"/> × <paramref name="areaHeight"/>.
    /// It goes on the <paramref name="placement"/> side of <paramref name="target"/>, flips to the other side when it
    /// would leave the area, and is kept inside it. With no target, or <see cref="TourPlacement.Centre"/>, it is centred.
    /// </summary>
    public static (double X, double Y) PlaceCard(
        TourRect? target, double cardWidth, double cardHeight, double areaWidth, double areaHeight, TourPlacement placement)
    {
        double x;
        double y;
        if (target is not { } t || placement == TourPlacement.Centre)
        {
            x = (areaWidth - cardWidth) / 2;
            y = (areaHeight - cardHeight) / 2;
        }
        else if (placement == TourPlacement.Right)
        {
            x = t.Right + Gap;
            y = t.CenterY - cardHeight / 2;
            if (x + cardWidth > areaWidth - Margin)
            {
                x = t.X - Gap - cardWidth;
                if (x < Margin)
                {
                    // Neither side has room: under the target, or over it when that is short of room too.
                    x = t.CenterX - cardWidth / 2;
                    y = Vertical(t, cardHeight, areaHeight, below: true);
                }
            }
        }
        else
        {
            x = t.CenterX - cardWidth / 2;
            y = Vertical(t, cardHeight, areaHeight, below: placement == TourPlacement.Below);
        }
        return (Clamp(x, cardWidth, areaWidth), Clamp(y, cardHeight, areaHeight));
    }

    /// <summary>Whether the right-click menu and the card fit side by side, with the gaps, in this width.</summary>
    public static bool MenuFits(double areaWidth, double cardWidth) =>
        MenuInset + MenuWidth + Gap + cardWidth + Margin <= areaWidth;

    private static double Vertical(TourRect target, double cardHeight, double areaHeight, bool below)
    {
        double under = target.Bottom + Gap;
        double over = target.Y - Gap - cardHeight;
        bool fitsUnder = under + cardHeight <= areaHeight - Margin;
        bool fitsOver = over >= Margin;
        if (below)
        {
            return fitsUnder || !fitsOver ? under : over;
        }
        return fitsOver || !fitsUnder ? over : under;
    }

    private static double Clamp(double value, double size, double area) =>
        Math.Max(Margin, Math.Min(value, area - size - Margin));
}
