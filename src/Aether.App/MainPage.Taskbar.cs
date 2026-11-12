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
    // False while the queue is a drop of several files or folders, which is kept rather than read from a folder.
    private bool _queueIsFolder = true;

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
            _queueIsFolder = true;
        }
        else if (!_queueIsFolder)
        {
            return;
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
            Open(target, fromQueue: true);
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
        if (!_queueIsFolder)
        {
            return step(_playlist);
        }
        string[]? videos = await Task.Run(() => VideosBeside(current));
        if (videos is null || !_isLoaded || _session is null || _currentPath != current)
        {
            return null;
        }
        _playlist.Load(videos, current);
        return step(_playlist);
    }

    /// <summary>Plays a drop of several files or folders (13.2) as a queue of its own, from its first video.</summary>
    private async void OpenDropped(string[] files, string[] folders)
    {
        string[] queue = await Task.Run(() => FolderPlaylist.Dropped(files, folders, ListFolder));
        if (queue.Length == 0 || !_isLoaded || _session is null)
        {
            return;
        }
        _playlist.Load(queue, queue[0]);
        _queueIsFolder = false;
        Open(queue[0]);
    }

    private static string[]? VideosBeside(string path) =>
        Path.GetDirectoryName(path) is { Length: > 0 } folder ? FolderPlaylist.Videos(ListFolder(folder)) : null;

    private static string[] ListFolder(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return []; // the folder went away or is unreadable: nothing from it
        }
    }
}
