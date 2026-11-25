using System.Text.Json;
using Aether.Core.Interop.Mpv;

namespace Aether.Core.Playback.Audio;

/// <summary>One audio output endpoint reported by libmpv's <c>audio-device-list</c> property.</summary>
public sealed record AudioDevice(string Name, string Description);

/// <summary>A selectable item for the audio output device pickers, backed by a setting value (<c>""</c> = system default).</summary>
public sealed record AudioDeviceOption(string Value, string Label, bool IsAvailable = true);

/// <summary>Parses and maps libmpv's <c>audio-device-list</c> endpoints for Settings and the Tracks flyout.</summary>
public sealed class AudioDeviceList
{
    public static AudioDeviceList Empty { get; } = new([]);

    public AudioDeviceList(IReadOnlyList<AudioDevice> devices)
    {
        Devices = devices;
    }

    public IReadOnlyList<AudioDevice> Devices { get; }

    /// <summary>Reads <c>audio-device-list</c> off the calling thread; returns <see cref="Empty"/> while the core shuts down.</summary>
    public static AudioDeviceList Read(MpvContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return Parse(context.GetPropertyString("audio-device-list"));
        }
        catch (Exception exception) when (exception is MpvException or ObjectDisposedException)
        {
            return Empty;
        }
    }

    /// <summary>Parses libmpv's <c>audio-device-list</c> JSON array cleanly without reflection.</summary>
    public static AudioDeviceList Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Empty;
            }

            var devices = new List<AudioDevice>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (JsonElement entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object
                    || !entry.TryGetProperty("name", out JsonElement nameElement)
                    || nameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string? name = nameElement.GetString()?.Trim();
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                {
                    continue;
                }

                string description = entry.TryGetProperty("description", out JsonElement descElement)
                    && descElement.ValueKind == JsonValueKind.String
                    && descElement.GetString() is { Length: > 0 } desc
                    ? desc.Trim()
                    : name;

                devices.Add(new AudioDevice(name, description));
            }

            return new AudioDeviceList(devices);
        }
        catch (JsonException)
        {
            return Empty;
        }
    }

    /// <summary>Maps raw libmpv device identifiers (<c>"auto"</c>, null, blank) onto Aether's setting convention (<c>""</c>).</summary>
    public static string NormalizeSettingValue(string? value)
    {
        string trimmed = (value ?? string.Empty).Trim();
        return string.Equals(trimmed, "auto", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : trimmed;
    }

    /// <summary>Maps Aether's setting value (<c>""</c> for system default) onto libmpv's <c>audio-device</c> value.</summary>
    public static string ToMpvValue(string? settingValue)
    {
        string normalized = NormalizeSettingValue(settingValue);
        return normalized.Length == 0 ? "auto" : normalized;
    }

    /// <summary>
    /// Builds display options with System default first, enumerated active endpoints (omitting duplicate <c>auto</c>),
    /// and the saved endpoint appended as unavailable when unplugged.
    /// </summary>
    public IReadOnlyList<AudioDeviceOption> BuildOptions(
        string? currentSettingValue,
        string systemDefaultLabel,
        string unavailableFormat)
    {
        string current = NormalizeSettingValue(currentSettingValue);
        var options = new List<AudioDeviceOption>(Devices.Count + 2)
        {
            new(string.Empty, systemDefaultLabel, IsAvailable: true),
        };

        bool foundCurrent = current.Length == 0;
        foreach (AudioDevice device in Devices)
        {
            string normalized = NormalizeSettingValue(device.Name);
            if (normalized.Length == 0)
            {
                continue;
            }

            string label = string.IsNullOrWhiteSpace(device.Description) ? normalized : device.Description;
            options.Add(new AudioDeviceOption(normalized, label, IsAvailable: true));
            if (string.Equals(normalized, current, StringComparison.Ordinal))
            {
                foundCurrent = true;
            }
        }

        if (!foundCurrent)
        {
            string fallbackLabel = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                unavailableFormat,
                current);
            options.Add(new AudioDeviceOption(current, fallbackLabel, IsAvailable: false));
        }

        return options;
    }
}
