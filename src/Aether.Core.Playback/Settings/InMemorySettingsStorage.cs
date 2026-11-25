namespace Aether.Core.Playback.Settings;

/// <summary>
/// Thread-safe in-memory <see cref="ISettingsStorage"/> used by isolated runs (such as visual audit mode)
/// so theme, layout, recents, and clip changes never touch user files in <c>%LOCALAPPDATA%\Aether</c>.
/// </summary>
public sealed class InMemorySettingsStorage : ISettingsStorage
{
    private readonly Lock _gate = new();
    private string? _content;

    public InMemorySettingsStorage(string? initialContent = null)
    {
        _content = initialContent;
    }

    public string? Read()
    {
        lock (_gate)
        {
            return _content;
        }
    }

    public void Write(string content)
    {
        lock (_gate)
        {
            _content = content;
        }
    }
}
