namespace Aether.Core.Playback.Windowing;

/// <summary>A window rectangle in screen pixels.</summary>
public readonly record struct WindowRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
}

/// <summary>
/// The window as it was left: its normal (restored) bounds, whether it was maximised, and where the
/// picture-in-picture window last was (null until it has been used).
/// </summary>
public sealed record SavedWindow(WindowRect Bounds, bool IsMaximized, WindowRect? PictureInPicture = null);
