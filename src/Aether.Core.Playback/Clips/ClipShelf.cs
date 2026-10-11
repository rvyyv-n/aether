using System.Text;
using System.Text.Json;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Clips;

/// <summary>An exported clip in the drawer. <see cref="Length"/> is in seconds.</summary>
public sealed record SavedClip(string Path, long SizeBytes, double Length, DateTimeOffset SavedAt)
{
    public string Name => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The clips the drawer lists (5.3), newest first, at most <see cref="MaxItems"/>. Dismissing a row only
/// drops it from the list; the file stays on disk. Persisted as JSON through an <see cref="ISettingsStorage"/>
/// after every change. Thread-safe; loading never throws, and a failed write keeps the list in memory.
/// </summary>
public sealed class ClipShelf(ISettingsStorage storage)
{
    public const int MaxItems = 20;
    private const int FileVersion = 1;

    private readonly object _gate = new();
    private SavedClip[] _items = [];

    /// <summary>Raised on the thread that made the change, outside the shelf's lock.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<SavedClip> Items
    {
        get { lock (_gate) { return _items; } }
    }

    public void Load()
    {
        SavedClip[] loaded;
        try
        {
            loaded = Parse(storage.Read());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            loaded = [];
        }
        lock (_gate)
        {
            _items = loaded;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Add(SavedClip clip) => Update(items =>
        [clip, .. items.Where(i => !SamePath(i.Path, clip.Path)).Take(MaxItems - 1)]);

    public void Remove(string path) => Update(items => items.Where(i => !SamePath(i.Path, path)).ToArray());

    private void Update(Func<SavedClip[], SavedClip[]> change)
    {
        string json;
        lock (_gate)
        {
            SavedClip[] next = change(_items);
            if (next.SequenceEqual(_items))
            {
                return;
            }
            _items = next;
            json = Serialize(next);
        }
        try
        {
            storage.Write(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The drawer still lists it this session; it is written again on the next change.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static bool SamePath(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static string Serialize(IReadOnlyList<SavedClip> items)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FileVersion);
            writer.WriteStartArray("items");
            foreach (SavedClip clip in items)
            {
                writer.WriteStartObject();
                writer.WriteString("path", clip.Path);
                writer.WriteNumber("size", clip.SizeBytes);
                writer.WriteNumber("length", clip.Length);
                writer.WriteString("saved", clip.SavedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Skips entries that are malformed; a corrupt file gives an empty list.</summary>
    internal static SavedClip[] Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("items", out JsonElement items)
                || items.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var result = new List<SavedClip>();
            foreach (JsonElement entry in items.EnumerateArray())
            {
                if (result.Count == MaxItems)
                {
                    break;
                }
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("path", out JsonElement p) && p.ValueKind == JsonValueKind.String
                    && p.GetString() is { Length: > 0 } path
                    && entry.TryGetProperty("saved", out JsonElement s) && s.TryGetDateTimeOffset(out DateTimeOffset savedAt)
                    && !result.Any(i => SamePath(i.Path, path)))
                {
                    long size = entry.TryGetProperty("size", out JsonElement z) && z.ValueKind == JsonValueKind.Number
                        && z.TryGetInt64(out long bytes) && bytes > 0
                        ? bytes
                        : 0;
                    double length = entry.TryGetProperty("length", out JsonElement l) && l.ValueKind == JsonValueKind.Number
                        && l.TryGetDouble(out double seconds) && double.IsFinite(seconds) && seconds > 0
                        ? seconds
                        : 0;
                    result.Add(new SavedClip(path, size, length, savedAt));
                }
            }
            return [.. result];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\clips.json</c>, beside the settings file.</summary>
    public static string DefaultPath() =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "clips.json");
}
