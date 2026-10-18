using Aether.Core.Playback;

namespace Aether_App;

/// <summary>
/// The previous, Play/Pause and next buttons under the taskbar preview (11.14). The window draws them; this
/// decides what they show and what they do. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // The files either side of the open one; empty for a link or while the folder is still being read.
    private FolderNeighbours _neighbours;

    private void UpdateTaskbar(string? fileName)
    {
        TaskbarControls controls = TaskbarControls.From(_session?.ViewModel.Status ?? PlaybackStatus.Idle, _neighbours);
        _host?.SetTaskbar(fileName, controls);
    }

    private void OnTaskbarCommand(object? sender, TaskbarCommand command)
    {
        switch (command)
        {
            case TaskbarCommand.PlayPause:
                TogglePause();
                break;
            case TaskbarCommand.Previous:
                OpenNeighbour(forward: false);
                break;
            case TaskbarCommand.Next:
                OpenNeighbour(forward: true);
                break;
        }
    }

    /// <summary>Reads the folder of a newly opened local file to learn which of the buttons have somewhere to go.</summary>
    private async void RefreshNeighbours(string path, bool isLink)
    {
        _neighbours = default;
        if (isLink || !Path.IsPathFullyQualified(path))
        {
            return;
        }
        FolderNeighbours found = await Task.Run(() => NeighboursOnDisk(path));
        // The user may have opened something else, or closed the page, while the folder was read.
        if (_isLoaded && _currentPath == path)
        {
            _neighbours = found;
            Render();
        }
    }

    private async void OpenNeighbour(bool forward)
    {
        if (_currentPath is not { } current || _currentWebLink is not null || !Path.IsPathFullyQualified(current))
        {
            return;
        }
        // Read again rather than trusting the earlier listing: files may have come and gone since.
        FolderNeighbours found = await Task.Run(() => NeighboursOnDisk(current));
        string? target = forward ? found.Next : found.Previous;
        if (target is not null && _isLoaded && _session is not null && _currentPath == current)
        {
            Open(target);
        }
    }

    private static FolderNeighbours NeighboursOnDisk(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return default;
        }
        try
        {
            return FolderPlaylist.NeighboursOf(path, Directory.EnumerateFiles(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default; // the folder went away or is unreadable: no buttons
        }
    }
}
