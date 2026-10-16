using System.Text;
using System.Text.Json;

namespace Aether.Core.Playback.Settings;

/// <summary>Raised after a setting changes. <see cref="Key"/> is the persisted key.</summary>
public sealed class SettingChangedEventArgs(string key) : EventArgs
{
    public string Key { get; } = key;
}

/// <summary>
/// Holds the current <see cref="AetherSettings"/>, loads and saves them as JSON through an <see cref="ISettingsStorage"/>.
/// Thread-safe. Loading never throws: a missing or corrupt file gives defaults, and an invalid value falls back to the
/// default for that setting only. Settings flagged as needing a restart set <see cref="PendingRestart"/> when they
/// differ from the value present at the last <see cref="Load"/> or <see cref="MarkStarted"/>.
/// </summary>
public sealed class SettingsStore(ISettingsStorage storage)
{
    private const int FileVersion = 1;

    private readonly object _gate = new();
    private readonly object _saveGate = new();
    private AetherSettings _current = new();
    private AetherSettings _loaded = new();

    /// <summary>Raised on the thread that made the change, outside the store's lock.</summary>
    public event EventHandler<SettingChangedEventArgs>? Changed;

    public AetherSettings Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>True when any restart-flagged setting differs from its value at load time.</summary>
    public bool PendingRestart
    {
        get
        {
            lock (_gate)
            {
                return SettingDefinitions.All.Any(d => d.RequiresRestart && !Equals(d.GetValue(_current), d.GetValue(_loaded)));
            }
        }
    }

    /// <summary>Whether the setting only takes effect after a restart. Unknown keys return false.</summary>
    public bool RequiresRestart(string key) => SettingDefinitions.TryGet(key, out var d) && d.RequiresRestart;

    /// <summary>True when the setting is restart-flagged and its value differs from the one at load time.</summary>
    public bool IsRestartPending(string key)
    {
        if (!SettingDefinitions.TryGet(key, out var d) || !d.RequiresRestart)
        {
            return false;
        }
        lock (_gate)
        {
            return !Equals(d.GetValue(_current), d.GetValue(_loaded));
        }
    }

    /// <summary>True when the setting equals its default. Unknown keys return true.</summary>
    public bool IsDefault(string key) =>
        !SettingDefinitions.TryGet(key, out var d) || Equals(d.GetValue(Current), d.GetValue(SettingDefinitions.Defaults));

    public object? Get(string key) => SettingDefinitions.TryGet(key, out var d) ? d.GetValue(Current) : null;

    /// <summary>Sets a value. Returns false (and changes nothing) for an unknown key, wrong type or disallowed value.</summary>
    public bool Set<T>(string key, T value)
    {
        if (!SettingDefinitions.TryGet(key, out var definition))
        {
            return false;
        }
        bool changed;
        lock (_gate)
        {
            AetherSettings? next = definition.TryWith(_current, value);
            if (next is null)
            {
                return false;
            }
            changed = !Equals(definition.GetValue(next), definition.GetValue(_current));
            _current = next;
        }
        if (changed)
        {
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }
        return true;
    }

    /// <summary>Restores one setting to its default. Returns false for an unknown key.</summary>
    public bool Reset(string key)
    {
        if (!SettingDefinitions.TryGet(key, out var definition))
        {
            return false;
        }
        bool changed;
        lock (_gate)
        {
            AetherSettings next = definition.WithDefault(_current);
            changed = !Equals(definition.GetValue(next), definition.GetValue(_current));
            _current = next;
        }
        if (changed)
        {
            Changed?.Invoke(this, new SettingChangedEventArgs(key));
        }
        return true;
    }

    public void ResetAll()
    {
        AetherSettings before;
        AetherSettings after;
        lock (_gate)
        {
            before = _current;
            // Not a preference: resetting must not replay the first-run tour.
            after = new AetherSettings { TourSeen = before.TourSeen };
            _current = after;
        }
        RaiseDifferences(before, after);
    }

    /// <summary>Reads the stored settings and makes them current. Never throws.</summary>
    public void Load()
    {
        AetherSettings loaded = Parse(ReadSafely());
        AetherSettings before;
        lock (_gate)
        {
            before = _current;
            _current = loaded;
            _loaded = loaded;
        }
        RaiseDifferences(before, loaded);
    }

    /// <summary>Raised after <see cref="MarkStarted"/>, on the calling thread, so restart notes can hide.</summary>
    public event EventHandler? Started;

    /// <summary>
    /// The engine was restarted with <paramref name="started"/>: restart-flagged settings now compare against those
    /// values, so <see cref="PendingRestart"/> stays true only for changes made since.
    /// </summary>
    public void MarkStarted(AetherSettings started)
    {
        ArgumentNullException.ThrowIfNull(started);
        lock (_gate)
        {
            _loaded = started;
        }
        Started?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Writes the current settings. Throws if the storage fails, so the caller can report it.</summary>
    public void Save()
    {
        // Callers save on pool threads after every change. One write at a time, each taking the snapshot inside the
        // lock, so overlapping saves cannot share the temp file and the last write always holds the newest values.
        lock (_saveGate)
        {
            storage.Write(Serialize(Current));
        }
    }

    private string? ReadSafely()
    {
        try
        {
            return storage.Read();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private void RaiseDifferences(AetherSettings before, AetherSettings after)
    {
        foreach (SettingDefinition d in SettingDefinitions.All)
        {
            if (!Equals(d.GetValue(before), d.GetValue(after)))
            {
                Changed?.Invoke(this, new SettingChangedEventArgs(d.Key));
            }
        }
    }

    internal static string Serialize(AetherSettings settings)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", FileVersion);
            foreach (SettingDefinition d in SettingDefinitions.All)
            {
                d.Write(writer, settings);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    internal static AetherSettings Parse(string? json)
    {
        var result = new AetherSettings();
        if (string.IsNullOrWhiteSpace(json))
        {
            return result;
        }
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return result;
            }
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (SettingDefinitions.TryGet(property.Name, out var definition))
                {
                    result = definition.TryRead(result, property.Value) ?? result;
                }
            }
            // A file saved before the tour existed belongs to someone who already knows the app.
            if (!document.RootElement.TryGetProperty(SettingKeys.TourSeen, out _))
            {
                result = result with { TourSeen = true };
            }
        }
        catch (JsonException)
        {
            return new AetherSettings();
        }
        return result;
    }
}
