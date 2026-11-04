using Aether.Core.Playback.Audio;

namespace Aether.Core.Playback.Settings;

/// <summary>
/// Keeps a running core's live properties in line with the settings. Each <see cref="Apply"/> sends only what differs
/// from the last send, so a burst of changes (Reset all) costs one pass. Any thread; calls are serialised, and
/// <c>send</c> must not block.
/// </summary>
public sealed class MpvSettingsSync(Action<string[]> send)
{
    private readonly Action<string[]> _send = send ?? throw new ArgumentNullException(nameof(send));
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _sent = new(StringComparer.Ordinal);
    private bool _normalising;
    private string? _deinterlacer;
    // The curve the running equalizer has, and the one its entry in mpv's filter list was added with: slider moves
    // reach the running filter only, so the two part until PrepareForFile.
    private EqualizerCurve? _equalizer;
    private EqualizerCurve? _equalizerEntry;

    /// <summary>Sends the properties of <see cref="MpvSettingsMap.RuntimePlan"/> that changed since the last call.</summary>
    /// <exception cref="Aether.Core.Interop.Mpv.MpvException">The core rejected a command outright, e.g. while shutting down.</exception>
    public void Apply(AetherSettings settings, bool onBattery)
    {
        MpvRuntimePlan plan = MpvSettingsMap.RuntimePlan(settings, onBattery);
        lock (_gate)
        {
            foreach (KeyValuePair<string, string> property in plan.Properties)
            {
                if (_sent.TryGetValue(property.Key, out string? last) && last == property.Value)
                {
                    continue;
                }
                _send(["set", property.Key, property.Value]);
                _sent[property.Key] = property.Value;
            }

            if (plan.NormaliseVolume != _normalising)
            {
                foreach (string[] command in MpvSettingsMap.NormaliseCommands(plan.NormaliseVolume))
                {
                    _send(command);
                }
                _normalising = plan.NormaliseVolume;
            }

            if (plan.DeinterlaceFilter != _deinterlacer)
            {
                foreach (string[] command in MpvSettingsMap.DeinterlaceCommands(plan.DeinterlaceFilter))
                {
                    _send(command);
                }
                _deinterlacer = plan.DeinterlaceFilter;
            }

            if (!Equals(plan.Equalizer, _equalizer))
            {
                foreach (string[] command in Equalizer.Commands(_equalizer, plan.Equalizer))
                {
                    _send(command);
                }
                if (_equalizer is null || plan.Equalizer is null)
                {
                    _equalizerEntry = plan.Equalizer;
                }
                _equalizer = plan.Equalizer;
            }
        }
    }

    /// <summary>
    /// Before a file loads: mpv builds its filters from their entries, so an equalizer moved live since it was added is
    /// added again with where the sliders are now.
    /// </summary>
    public void PrepareForFile()
    {
        lock (_gate)
        {
            if (_equalizer is not null && !_equalizer.Equals(_equalizerEntry))
            {
                foreach (string[] command in Equalizer.Commands(null, _equalizer))
                {
                    _send(command);
                }
                _equalizerEntry = _equalizer;
            }
        }
    }
}
