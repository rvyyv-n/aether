namespace Aether.Core.Playback.Windowing;

/// <summary>A window rectangle in screen pixels.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>The window as it was left: its normal (restored) bounds, and whether it was maximised.</summary>
public sealed record SavedWindow(WindowRect Bounds, bool IsMaximized);
