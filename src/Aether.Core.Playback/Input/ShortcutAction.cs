namespace Aether.Core.Playback.Input;

/// <summary>
/// The player actions whose keys can be changed. Escape (leave full screen or close) and the frame-step keys
/// (comma and full stop) are not here on purpose: they stay fixed.
/// </summary>
public enum ShortcutAction
{
    OpenFile,
    OpenUrl,
    SkipBack,
    SkipForward,
    SkipBackFine,
    SkipForwardFine,
    SkipBackLong,
    SkipForwardLong,
    SetIn,
    SetOut,
    PlayPause,
    FullScreen,
    Mute,
    Loop,
    AlwaysOnTop,
    Diagnostics,
    Settings,
    CopyFrame,
    SaveFrame,
    ExportClip,
    Sidebar,
}
