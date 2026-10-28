using Aether.Core.Playback;

namespace Aether_App;

/// <summary>
/// The previous, Play/Pause and next buttons under the taskbar preview (11.14). The window draws them; this
/// decides what they show and what they do. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // The open file's folder, in play order; empty for a link.
    private readonly PlaylistStore _playlist = new();

    private void UpdateTaskbar(string? fileName)
    {
        TaskbarControls controls = TaskbarControls.From(
            _session?.ViewModel.Status ?? PlaybackStatus.Idle, _playlist.CanGoPrevious, _playlist.CanGoNext);
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

    /// <summary>Queues up the folder of a newly opened local file, so the buttons know where they can go.</summary>
    private async void RefreshQueue(string path, bool isLink)
    {
        // A file the queue moved to keeps the queue's order; any other file starts a new one.
        if (!string.Equals(_playlist.Current, path, StringComparison.OrdinalIgnoreCase))
        {
            _playlist.Clear();
        }
        if (isLink || !Path.IsPathFullyQualified(path))
        {
            return;
        }
        string[]? videos = await Task.Run(() => VideosBeside(path));
        // The user may have opened something else, or closed the page, while the folder was read.
        if (videos is not null && _isLoaded && _currentPath == path)
        {
            _playlist.Load(videos, path);
            Render();
        }
    }

    private async void OpenNeighbour(bool forward)
    {
        if (await StepQueue(queue => forward ? queue.Next(ended: false) : queue.Previous()) is { } target)
        {
            Open(target);
        }
    }

    /// <summary>
    /// Reads the open file's folder again rather than trusting the queue, since files may have come and gone, then
    /// moves the queue with <paramref name="step"/>. Null for a link, an unreadable folder, or nowhere to go.
    /// </summary>
    private async Task<string?> StepQueue(Func<PlaylistStore, string?> step)
    {
        if (_currentPath is not { } current || _currentWebLink is not null || !Path.IsPathFullyQualified(current))
        {
            return null;
        }
        string[]? videos = await Task.Run(() => VideosBeside(current));
        if (videos is null || !_isLoaded || _session is null || _currentPath != current)
        {
            return null;
        }
        _playlist.Load(videos, current);
        return step(_playlist);
    }

    private static string[]? VideosBeside(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return null;
        }
        try
        {
            return FolderPlaylist.Videos(Directory.EnumerateFiles(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // the folder went away or is unreadable: nowhere to go
        }
    }
}
