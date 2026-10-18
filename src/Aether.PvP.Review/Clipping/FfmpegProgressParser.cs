using System.Globalization;

namespace Aether.PvP.Review.Clipping;

// Reads the key=value blocks that `-progress pipe:1` prints; each block ends with progress=continue|end.
public sealed class FfmpegProgressParser
{
    private readonly double _durationSeconds;
    private double _outSeconds;
    private double _speed;

    public FfmpegProgressParser(double durationSeconds)
    {
        _durationSeconds = durationSeconds;
    }

    // Returns a value when the line closes a block, otherwise null.
    public ClipProgress? Feed(string line)
    {
        int eq = line.IndexOf('=');
        if (eq <= 0) return null;

        string key = line[..eq].Trim();
        string value = line[(eq + 1)..].Trim();

        switch (key)
        {
            // out_time_ms is really microseconds, the same as out_time_us.
            case "out_time_us" or "out_time_ms":
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros))
                    _outSeconds = Math.Max(0, micros / 1_000_000.0);
                return null;
            case "speed":
                _speed = double.TryParse(value.TrimEnd('x'), NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s > 0 ? s : 0;
                return null;
            case "progress":
                return value == "end" ? new ClipProgress(1.0, _speed, TimeSpan.Zero) : Snapshot();
            default:
                return null;
        }
    }

    public async Task ReadAsync(TextReader reader, IProgress<ClipProgress>? progress, CancellationToken ct = default)
    {
        string? line;
        while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if (Feed(line) is { } update)
                progress?.Report(update);
        }
    }

    private ClipProgress Snapshot()
    {
        double fraction = _durationSeconds > 0 ? Math.Clamp(_outSeconds / _durationSeconds, 0, 1) : 0;
        TimeSpan? remaining = _speed > 0 && _durationSeconds > 0
            ? TimeSpan.FromSeconds(Math.Max(0, _durationSeconds - _outSeconds) / _speed)
            : null;
        return new ClipProgress(fraction, _speed, remaining);
    }
}
