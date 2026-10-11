using System.Text;

namespace Aether.Core.Playback.Settings;

/// <summary>Settings file on disk. Writes a temp file beside the target, then replaces the target.</summary>
public sealed class FileSettingsStorage(string path) : ISettingsStorage
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string Path { get; } = path;

    public string? Read() => File.Exists(Path) ? File.ReadAllText(Path, Utf8NoBom) : null;

    public void Write(string content)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        string temp = Path + ".tmp";
        File.WriteAllText(temp, content, Utf8NoBom);
        if (File.Exists(Path))
        {
            File.Replace(temp, Path, null);
        }
        else
        {
            File.Move(temp, Path);
        }
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\settings.json</c>; works packaged and unpackaged.</summary>
    public static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "settings.json");
}
