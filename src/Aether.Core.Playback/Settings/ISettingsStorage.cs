namespace Aether.Core.Playback.Settings;

/// <summary>Where the settings JSON lives. Implementations must make <see cref="Write"/> atomic.</summary>
public interface ISettingsStorage
{
    /// <summary>The stored text, or null when nothing has been saved yet.</summary>
    string? Read();

    /// <summary>Replaces the stored text; a reader sees the old or the new text, never a partial file.</summary>
    void Write(string content);
}
