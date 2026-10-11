using System.Text;
using System.Text.Json;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Recents;

/// <summary>One opened file or link. <see cref="Duration"/> is in seconds, 0 until the file has loaded.</summary>
public sealed record RecentItem(string Location, string DisplayName, DateTimeOffset OpenedAt, double Duration = 0)
{
    public bool IsLink => WebLink.IsValid(Location);
}

/// <summary>
/// Recently opened files and links, newest first, at most <see cref="MaxItems"/> (2.10). Persisted as JSON
/// through an <see cref="ISettingsStorage"/> after every change. Thread-safe; loading never throws, and a
/// failed write keeps the list in memory.
/// </summary>
public sealed class RecentsStore(ISettingsStorage storage)
{
    public const int MaxItems = 3;
    private const int FileVersion = 1;

    private readonly object _gate = new();
    private RecentItem[] _items = [];

    /// <summary>Raised on the thread that made the change, outside the store's lock.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<RecentItem> Items
    {
        get { lock (_gate) { return _items; } }
    }

    public void Load()
    {
        RecentItem[] loaded;
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

    /// <summary>Moves the location to the top, keeping a known duration when it was already listed.</summary>
    public void Add(string location, string displayName, DateTimeOffset openedAt) => Update(items =>
    {
        RecentItem? existing = items.FirstOrDefault(i => SameLocation(i.Location, location));
        var added = new RecentItem(location, displayName, openedAt, existing?.Duration ?? 0);
        return [added, .. items.Where(i => !SameLocation(i.Location, location)).Take(MaxItems - 1)];
    });

    public void SetDuration(string location, double seconds) => Update(items =>
        items.Select(i => SameLocation(i.Location, location) ? i with { Duration = Math.Max(0, seconds) } : i).ToArray());

    public void Remove(string location) => Update(items => items.Where(i => !SameLocation(i.Location, location)).ToArray());

    public void Clear() => Update(_ => []);

    private void Update(Func<RecentItem[], RecentItem[]> change)
    {
        string json;
        lock (_gate)
        {
            RecentItem[] next = change(_items);
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
            // The list still shows this session; it is written again on the next change.
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // Windows paths compare without case; links keep theirs.
    private static bool SameLocation(string a, string b) =>
        string.Equals(a, b, WebLink.IsValid(a) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    internal static string Serialize(IReadOnlyList<RecentItem> items)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FileVersion);
            writer.WriteStartArray("items");
            foreach (RecentItem item in items)
            {
                writer.WriteStartObject();
                writer.WriteString("location", item.Location);
                writer.WriteString("name", item.DisplayName);
                writer.WriteString("opened", item.OpenedAt);
                writer.WriteNumber("duration", item.Duration);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Skips entries that are malformed; a corrupt file gives an empty list.</summary>
    internal static RecentItem[] Parse(string? json)
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
            var result = new List<RecentItem>(MaxItems);
            foreach (JsonElement entry in items.EnumerateArray())
            {
                if (result.Count == MaxItems)
                {
                    break;
                }
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.String
                    && location.GetString() is { Length: > 0 } path
                    && entry.TryGetProperty("opened", out JsonElement opened) && opened.TryGetDateTimeOffset(out DateTimeOffset openedAt)
                    && !result.Any(i => SameLocation(i.Location, path)))
                {
                    string name = entry.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String
                        ? n.GetString() ?? string.Empty
                        : string.Empty;
                    double duration = entry.TryGetProperty("duration", out JsonElement d) && d.TryGetDouble(out double seconds)
                        && double.IsFinite(seconds) && seconds > 0
                        ? seconds
                        : 0;
                    result.Add(new RecentItem(path, name.Length > 0 ? name : Path.GetFileName(path), openedAt, duration));
                }
            }
            return [.. result];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\recents.json</c>, beside the settings file.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "recents.json");
}
