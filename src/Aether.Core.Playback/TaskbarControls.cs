namespace Aether.Core.Playback;

/// <summary>A press on one of the taskbar thumbnail's buttons.</summary>
public enum TaskbarCommand
{
    Previous,
    PlayPause,
    Next,
}

/// <summary>
/// What the buttons under the taskbar preview show: previous and next follow the folder neighbours, the middle
/// button follows playback, and with no file open all three are disabled.
/// </summary>
public readonly record struct TaskbarControls(bool PreviousEnabled, PlayButtonAction PlayPause, bool PlayPauseEnabled, bool NextEnabled)
{
    public static TaskbarControls From(PlaybackStatus status, FolderNeighbours neighbours)
    {
        if (status == PlaybackStatus.Idle)
        {
            return new TaskbarControls(false, PlayButtonAction.Play, false, false);
        }
        return new TaskbarControls(
            neighbours.Previous is not null,
            TransportState.PlayButton(status),
            // Pause has nothing to act on until the file has loaded.
            status != PlaybackStatus.Loading,
            neighbours.Next is not null);
    }

    /// <summary>The window title, which the taskbar preview shows: the app name, then the file name when one is open.</summary>
    public static string Title(string appName, string? fileName) =>
        string.IsNullOrEmpty(fileName) ? appName : $"{appName} - {fileName}";
}
