using System.Text;
using System.Text.Json;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Windowing;

/// <summary>
/// Remembers where the main window was left, in its own small file rather than in the settings: the geometry is
/// not something the user edits, and a settings reset should not move the window. Loading never throws; a failed
/// write leaves the value in memory. Thread-safe.
/// </summary>
public sealed class WindowStateStore(ISettingsStorage storage)
{
    /// <summary>Smaller than this is a corrupt file or a collapsed window, not something to restore.</summary>
    public const int MinSize = 200;

    /// <summary>The picture-in-picture window is smaller; below this it is not restored.</summary>
    public const int MinPipSize = 100;
    private const int MaxSize = 32768;
    private const int FileVersion = 1;

    private readonly object _gate = new();
    private SavedWindow? _saved;

    public SavedWindow? Saved
    {
        get { lock (_gate) { return _saved; } }
    }

    public void Load()
    {
        SavedWindow? loaded;
        try
        {
            loaded = Parse(storage.Read());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            loaded = null;
        }
        lock (_gate)
        {
            _saved = loaded;
        }
    }

    /// <summary>Records the window; ignores bounds too small to be a real window.</summary>
    public void Save(SavedWindow window)
    {
        if (window.Bounds.Width < MinSize || window.Bounds.Height < MinSize)
        {
            return;
        }
        lock (_gate)
        {
            _saved = window;
        }
        try
        {
            storage.Write(Serialize(window));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The next close writes it again.
        }
    }

    internal static string Serialize(SavedWindow window)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FileVersion);
            writer.WriteNumber("x", window.Bounds.X);
            writer.WriteNumber("y", window.Bounds.Y);
            writer.WriteNumber("width", window.Bounds.Width);
            writer.WriteNumber("height", window.Bounds.Height);
            writer.WriteBoolean("maximized", window.IsMaximized);
            if (window.PictureInPicture is { } pip)
            {
                writer.WriteStartObject("pip");
                writer.WriteNumber("x", pip.X);
                writer.WriteNumber("y", pip.Y);
                writer.WriteNumber("width", pip.Width);
                writer.WriteNumber("height", pip.Height);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Null for an empty, corrupt or implausible file.</summary>
    internal static SavedWindow? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ParseRect(root, MinSize) is not { } bounds)
            {
                return null;
            }
            bool maximized = root.TryGetProperty("maximized", out JsonElement m) && m.ValueKind == JsonValueKind.True;
            // A bad pip entry only loses the picture-in-picture place, not the window.
            WindowRect? pip = root.TryGetProperty("pip", out JsonElement p) && p.ValueKind == JsonValueKind.Object
                ? ParseRect(p, MinPipSize)
                : null;
            return new SavedWindow(bounds, maximized, pip);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static WindowRect? ParseRect(JsonElement element, int minSize)
    {
        if (!TryInt(element, "x", out int x) || !TryInt(element, "y", out int y)
            || !TryInt(element, "width", out int width) || !TryInt(element, "height", out int height)
            || width < minSize || height < minSize || width > MaxSize || height > MaxSize
            || Math.Abs(x) > MaxSize * 4 || Math.Abs(y) > MaxSize * 4)
        {
            return null;
        }
        return new WindowRect(x, y, width, height);
    }

    private static bool TryInt(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\window.json</c>, beside the settings file.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "window.json");
}
