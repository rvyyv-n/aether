using System.Text;
using System.Text.Json;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Resume;

/// <summary>Where a file was left. <see cref="Position"/> and <see cref="Duration"/> are in seconds.</summary>
public sealed record ResumeEntry(string Location, double Position, double Duration, DateTimeOffset SavedAt);

/// <summary>
/// Remembers the playback position of each file (5.10), at most <see cref="MaxEntries"/>, dropping the
/// oldest first. A position in the first <see cref="SkipStart"/> or last <see cref="SkipEnd"/> seconds is
/// not worth resuming and clears the entry instead. Persisted as JSON through an <see cref="ISettingsStorage"/>
/// after every change. Thread-safe; loading never throws, and a failed write keeps the entries in memory.
/// </summary>
public sealed class ResumeStore(ISettingsStorage storage)
{
    public const int MaxEntries = 200;
    public const double SkipStart = 5;
    public const double SkipEnd = 10;
    private const int FileVersion = 1;

    private readonly object _gate = new();
    private ResumeEntry[] _entries = [];

    public IReadOnlyList<ResumeEntry> Entries
    {
        get { lock (_gate) { return _entries; } }
    }

    /// <summary>True when a position is worth coming back to: past the start and short of the end of a known length.</summary>
    public static bool IsWorthResuming(double position, double duration) =>
        double.IsFinite(position) && double.IsFinite(duration)
        && duration > SkipStart + SkipEnd
        && position >= SkipStart && position <= duration - SkipEnd;

    public void Load()
    {
        ResumeEntry[] loaded;
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
            _entries = loaded;
        }
    }

    /// <summary>The position to resume <paramref name="location"/> at, or null to start from the beginning.</summary>
    public double? TryGet(string location)
    {
        lock (_gate)
        {
            ResumeEntry? entry = _entries.FirstOrDefault(e => SameLocation(e.Location, location));
            return entry is not null && IsWorthResuming(entry.Position, entry.Duration) ? entry.Position : null;
        }
    }

    /// <summary>Records where the file was left, or forgets it when the position is not worth resuming.</summary>
    public void Save(string location, double position, double duration, DateTimeOffset savedAt) => Update(entries =>
    {
        ResumeEntry[] others = entries.Where(e => !SameLocation(e.Location, location)).ToArray();
        return IsWorthResuming(position, duration)
            ? Prune([.. others, new ResumeEntry(location, position, duration, savedAt)])
            : others;
    });

    public void Remove(string location) => Update(entries => entries.Where(e => !SameLocation(e.Location, location)).ToArray());

    public void Clear() => Update(_ => []);

    private static ResumeEntry[] Prune(ResumeEntry[] entries) =>
        entries.Length <= MaxEntries
            ? entries
            : entries.OrderByDescending(e => e.SavedAt).Take(MaxEntries).ToArray();

    private void Update(Func<ResumeEntry[], ResumeEntry[]> change)
    {
        string json;
        lock (_gate)
        {
            ResumeEntry[] next = change(_entries);
            if (next.SequenceEqual(_entries))
            {
                return;
            }
            _entries = next;
            json = Serialize(next);
        }
        try
        {
            storage.Write(json);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // The positions still apply this session; they are written again on the next change.
        }
    }

    private static bool SameLocation(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    internal static string Serialize(IReadOnlyList<ResumeEntry> entries)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FileVersion);
            writer.WriteStartArray("entries");
            foreach (ResumeEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("location", entry.Location);
                writer.WriteNumber("position", entry.Position);
                writer.WriteNumber("duration", entry.Duration);
                writer.WriteString("saved", entry.SavedAt);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Skips entries that are malformed or repeated; a corrupt file gives an empty list.</summary>
    internal static ResumeEntry[] Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entries", out JsonElement entries)
                || entries.ValueKind != JsonValueKind.Array)
            {
                return [];
            }
            var result = new List<ResumeEntry>();
            foreach (JsonElement entry in entries.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object
                    && entry.TryGetProperty("location", out JsonElement location) && location.ValueKind == JsonValueKind.String
                    && location.GetString() is { Length: > 0 } path
                    && entry.TryGetProperty("position", out JsonElement p) && p.TryGetDouble(out double position)
                    && entry.TryGetProperty("duration", out JsonElement d) && d.TryGetDouble(out double duration)
                    && entry.TryGetProperty("saved", out JsonElement s) && s.TryGetDateTimeOffset(out DateTimeOffset saved)
                    && IsWorthResuming(position, duration)
                    && !result.Any(e => SameLocation(e.Location, path)))
                {
                    result.Add(new ResumeEntry(path, position, duration, saved));
                }
            }
            return Prune([.. result]);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary><c>%LOCALAPPDATA%\Aether\resume.json</c>, beside the settings file.</summary>
    public static string DefaultPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "resume.json");
}
