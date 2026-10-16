using Windows.Storage;
using Windows.System;

namespace Aether_App;

/// <summary>Opens File Explorer on a file's folder with the file selected, for the toasts and rows that offer Show in folder.</summary>
internal static class ShowInFolder
{
    /// <summary>False when the file or its folder has gone, so the caller can show it as missing.</summary>
    public static async Task<bool> TryAsync(string path)
    {
        if (Path.GetDirectoryName(path) is not { } directory)
        {
            return false;
        }
        try
        {
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(directory);
            var options = new FolderLauncherOptions();
            options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(path));
            await Launcher.LaunchFolderAsync(folder, options);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
