namespace Aether.Core.Playback;

/// <summary>
/// Which entries of the right-click menu (screen 9.1) can be used right now. Picture in picture stays off until
/// the work behind it lands (Pass 8).
/// </summary>
/// <param name="EngineReady">The engine is up, so a file or link can be opened.</param>
/// <param name="HasVideo">A file is open (playing, paused or ended).</param>
/// <param name="RecentCount">How many files and links the recents list holds.</param>
/// <param name="ClipCount">How many saved clips the drawer would list.</param>
/// <param name="HasWindow">The window host is attached, so Always on top has something to toggle.</param>
public sealed record ContextMenuState(bool EngineReady, bool HasVideo, int RecentCount, int ClipCount, bool HasWindow)
{
    public bool OpenFile => EngineReady;

    public bool OpenUrl => EngineReady;

    public bool Recent => EngineReady && RecentCount > 0;

    /// <summary>Audio and Subtitles open the tracks flyout, which has nothing to list without a file.</summary>
    public bool Audio => HasVideo;

    public bool Subtitles => HasVideo;

    public bool Speed => HasVideo;

    /// <summary>The video flyout and the frame grabs all need a picture to work on.</summary>
    public bool Video => HasVideo;

    public bool CopyFrame => HasVideo;

    public bool SaveFrame => HasVideo;

    public bool PictureInPicture => false;

    public bool AlwaysOnTop => HasWindow;

    public bool Diagnostics => true;

    public bool Settings => true;

    /// <summary>The drawer only exists with a video open, and has nothing to show until a clip is saved.</summary>
    public bool Clips => HasVideo && ClipCount > 0;

    /// <summary>Links cannot play until streaming lands, so there is no stream entry to open yet.</summary>
    public bool Stream => false;

    /// <summary>The Recent submenu shows one line per entry; a missing name falls back to the file or link itself.</summary>
    public static string RecentLabel(string displayName, string location) =>
        string.IsNullOrWhiteSpace(displayName) ? location : displayName;
}
