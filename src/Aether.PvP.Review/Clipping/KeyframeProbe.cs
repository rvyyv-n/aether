using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Aether.PvP.Review.Clipping;

public interface IKeyframeProbe
{
    // The last keyframe at or before the time, or null when none was found in a bounded window.
    Task<double?> FindAtOrBeforeAsync(string path, double seconds, CancellationToken ct);
}

// Where a Fast (stream copy) cut will really start. Only a short window before In is ever read,
// so a long file or a slow share never makes this expensive.
public sealed partial class KeyframeProbe : IKeyframeProbe
{
    private static readonly double[] Windows = [10, 30];
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(8);

    // ffprobe's read interval ends a little after In so a keyframe sitting exactly on In is included.
    private const double Overshoot = 0.05;
    private const double Tolerance = 0.001;

    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly Dictionary<(string Path, double Seconds), double?> _cache = [];
    private bool _noFfprobe;

    public KeyframeProbe(IProcessRunner runner, string ffmpegPath = "ffmpeg.exe", string? ffprobePath = null)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath ?? SiblingProbe(ffmpegPath);
    }

    public async Task<double?> FindAtOrBeforeAsync(string path, double seconds, CancellationToken ct)
    {
        if (!double.IsFinite(seconds) || seconds < 0) return null;
        lock (_cache)
        {
            if (_cache.TryGetValue((path, seconds), out var cached)) return cached;
        }

        double? found = null;
        foreach (double window in Windows)
        {
            double start = Math.Max(0, seconds - window);
            var times = await ProbeWindowAsync(path, start, seconds, ct).ConfigureAwait(false);
            if (times is null) break; // the tools failed: widening would fail the same way
            found = times.Where(t => t <= seconds + Tolerance).DefaultIfEmpty(double.NaN).Max();
            if (double.IsNaN(found.Value)) found = null;
            if (found is not null || start == 0) break;
        }

        lock (_cache) _cache[(path, seconds)] = found;
        return found;
    }

    // null means the probe could not run; an empty list means it ran and saw no keyframe.
    private async Task<List<double>?> ProbeWindowAsync(string path, double start, double seconds, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RunTimeout);
        try
        {
            if (!_noFfprobe)
            {
                try
                {
                    return await RunFfprobeAsync(path, start, seconds, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
                {
                    _noFfprobe = true;
                }
            }
            return await RunFfmpegAsync(path, start, seconds, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null; // timed out
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private async Task<List<double>?> RunFfprobeAsync(string path, double start, double seconds, CancellationToken ct)
    {
        var times = new List<double>();
        var outcome = await _runner.RunAsync(
            _ffprobePath,
            [
                "-v", "error", "-select_streams", "v:0", "-skip_frame", "nokey",
                "-show_entries", "frame=pts_time", "-of", "csv=p=0",
                "-read_intervals", Invariant($"{start}%{seconds + Overshoot}"),
                "-i", path
            ],
            async reader =>
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    if (ParseTime(line.Split(',')[0]) is { } t) times.Add(t);
                }
            },
            ct).ConfigureAwait(false);
        return outcome.ExitCode == 0 ? times : null;
    }

    private async Task<List<double>?> RunFfmpegAsync(string path, double start, double seconds, CancellationToken ct)
    {
        var outcome = await _runner.RunAsync(
            _ffmpegPath,
            [
                "-hide_banner", "-nostdin", "-skip_frame", "nokey",
                "-ss", Invariant($"{start}"), "-i", path, "-t", Invariant($"{seconds - start + Overshoot}"),
                "-map", "0:v:0", "-vf", "showinfo", "-f", "null", "-"
            ],
            null,
            ct).ConfigureAwait(false);
        if (outcome.ExitCode != 0) return null;
        return ParseShowInfo(outcome.StandardError).Select(t => t + start).ToList();
    }

    // showinfo lines read "... n:   0 pts: 1234 pts_time:1.234 ...".
    internal static IEnumerable<double> ParseShowInfo(string stderr) =>
        PtsTime().Matches(stderr).Select(m => ParseTime(m.Groups[1].Value)).OfType<double>();

    private static double? ParseTime(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var t) && double.IsFinite(t) ? t : null;

    private static string SiblingProbe(string ffmpegPath)
    {
        string? dir = Path.GetDirectoryName(ffmpegPath);
        return string.IsNullOrEmpty(dir) ? "ffprobe.exe" : Path.Combine(dir, "ffprobe.exe");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"pts_time:\s*(-?[\d.]+)")]
    private static partial Regex PtsTime();
}

// Follows the In point: waits for it to settle, cancels a probe the user has moved past, and reports
// the keyframe (or null) only for the In point that is still current.
public sealed class CutStartTracker : IDisposable
{
    private static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(250);

    private readonly IKeyframeProbe _probe;
    private readonly TimeSpan _debounce;
    private CancellationTokenSource? _current;
    private (string Path, double Seconds)? _tracked;

    public CutStartTracker(IKeyframeProbe probe, TimeSpan? debounce = null)
    {
        _probe = probe;
        _debounce = debounce ?? DefaultDebounce;
    }

    // Raised on a background thread. Null means unknown or not applicable.
    public event Action<double?>? Changed;

    // Pass a null path or In point to stop tracking.
    public void Track(string? path, double? inPoint)
    {
        (string, double)? next = path is not null && inPoint is { } seconds ? (path, seconds) : null;
        if (next == _tracked) return;
        _tracked = next;

        _current?.Cancel();
        _current?.Dispose();
        _current = null;
        Changed?.Invoke(null);
        if (next is not var (p, s)) return;

        var cts = new CancellationTokenSource();
        _current = cts;
        _ = RunAsync(p, s, cts.Token);
    }

    private async Task RunAsync(string path, double seconds, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_debounce, ct).ConfigureAwait(false);
            double? keyframe = await _probe.FindAtOrBeforeAsync(path, seconds, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            Changed?.Invoke(keyframe);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer In point.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            Changed?.Invoke(null);
        }
    }

    public void Dispose()
    {
        _current?.Cancel();
        _current?.Dispose();
        _current = null;
        _tracked = null;
    }
}

// How far before In a Fast cut will start, for the hint line and the lead-in on the seek bar.
public static class CutStart
{
    // About one frame; a keyframe closer than this to In is not worth showing.
    public const double MinimumLead = 0.04;

    public static double? Lead(double inPoint, double? keyframe) =>
        keyframe is { } k && inPoint - k > MinimumLead ? inPoint - k : null;

    // Like "1.4 s".
    public static string Format(double seconds, CultureInfo? culture = null) =>
        string.Create(culture ?? CultureInfo.CurrentCulture, $"{seconds:0.0} s");
}
