using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Aether.PvP.Review.Clipping;

public interface IKeyframeProbe
{
    // The last keyframe at or before the time, or null when none was found in a bounded window.
    Task<double?> FindAtOrBeforeAsync(string path, double seconds, CancellationToken ct);

    bool TryGetCached(string path, double seconds, out double? keyframe)
    {
        keyframe = null;
        return false;
    }
}

// Where a Fast (stream copy) cut will really start. Only a short window before In is ever read,
// so a long file or a slow share never makes this expensive.
public sealed partial class KeyframeProbe : IKeyframeProbe
{
    private static readonly double[] Windows = [10, 30];
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(8);

    public const double ForwardLookaheadSeconds = 5.0;

    // ffprobe's read interval ends a little after the target so a boundary keyframe is included.
    private const double Overshoot = 0.05;
    private const double Tolerance = 0.001;
    private const int MaxCachedFiles = 8;
    private const double IntervalEpsilon = 1e-9;
    private const double KeyframeDedupeEpsilon = 1e-6;

    private readonly IProcessRunner _runner;
    private readonly string _ffmpegPath;
    private readonly string _ffprobePath;
    private readonly Dictionary<string, LinkedListNode<FileCacheEntry>> _cache =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly LinkedList<FileCacheEntry> _lru = new();
    private bool _noFfprobe;

    public KeyframeProbe(IProcessRunner runner, string ffmpegPath = "ffmpeg.exe", string? ffprobePath = null)
    {
        _runner = runner;
        _ffmpegPath = ffmpegPath;
        _ffprobePath = ffprobePath ?? SiblingProbe(ffmpegPath);
    }

    public bool TryGetCached(string path, double seconds, out double? keyframe)
    {
        keyframe = null;
        if (string.IsNullOrWhiteSpace(path) || !double.IsFinite(seconds) || seconds < 0)
        {
            return false;
        }

        FileStamp? stamp = TryGetFileStamp(path);
        return TryGetFromCacheUnderLock(path, stamp, seconds, out keyframe);
    }

    public async Task<double?> FindAtOrBeforeAsync(string path, double seconds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(path) || !double.IsFinite(seconds) || seconds < 0)
        {
            return null;
        }
        ct.ThrowIfCancellationRequested();

        FileStamp? stamp = TryGetFileStamp(path);
        if (TryGetFromCacheUnderLock(path, stamp, seconds, out double? cached))
        {
            return cached;
        }

        double desiredEnd = seconds + ForwardLookaheadSeconds;
        foreach (double window in Windows)
        {
            double start = Math.Max(0, seconds - window);
            List<(double Start, double End)> gaps = GetUncoveredRanges(path, stamp, start, desiredEnd);

            bool failed = false;
            for (int i = gaps.Count - 1; i >= 0; i--)
            {
                var (gapStart, gapEnd) = gaps[i];
                List<double>? times = await ProbeWindowAsync(path, gapStart, gapEnd, ct).ConfigureAwait(false);
                if (times is null)
                {
                    failed = true;
                    break;
                }

                RecordProbeResult(path, stamp, gapStart, gapEnd, times);
                if (TryGetFromCacheUnderLock(path, stamp, seconds, out double? found))
                {
                    return found;
                }
            }

            if (failed)
            {
                break; // the tools failed: widening would fail the same way
            }

            if (TryGetFromCacheUnderLock(path, stamp, seconds, out double? resolvedAfterWindow))
            {
                return resolvedAfterWindow;
            }

            if (start == 0)
            {
                break;
            }
        }

        return null;
    }

    private bool TryGetFromCacheUnderLock(string path, FileStamp? stamp, double seconds, out double? keyframe)
    {
        keyframe = null;
        lock (_cache)
        {
            if (!_cache.TryGetValue(path, out LinkedListNode<FileCacheEntry>? node))
            {
                return false;
            }

            FileCacheEntry entry = node.Value;
            if (entry.Stamp != stamp)
            {
                _lru.Remove(node);
                _cache.Remove(path);
                return false;
            }

            if (!TryResolveFromEntry(entry, seconds, out keyframe))
            {
                return false;
            }

            _lru.Remove(node);
            _lru.AddFirst(node);
            return true;
        }
    }

    private List<(double Start, double End)> GetUncoveredRanges(string path, FileStamp? stamp, double start, double end)
    {
        lock (_cache)
        {
            if (!_cache.TryGetValue(path, out LinkedListNode<FileCacheEntry>? node))
            {
                return [(start, end)];
            }

            if (node.Value.Stamp != stamp)
            {
                _lru.Remove(node);
                _cache.Remove(path);
                return [(start, end)];
            }

            return ComputeUncoveredRanges(node.Value.Covered, start, end);
        }
    }

    private void RecordProbeResult(string path, FileStamp? stamp, double start, double end, List<double> times)
    {
        lock (_cache)
        {
            FileCacheEntry entry;
            if (_cache.TryGetValue(path, out LinkedListNode<FileCacheEntry>? node))
            {
                if (node.Value.Stamp != stamp)
                {
                    entry = new FileCacheEntry { Path = path, Stamp = stamp };
                    node.Value = entry;
                }
                else
                {
                    entry = node.Value;
                }

                _lru.Remove(node);
                _lru.AddFirst(node);
            }
            else
            {
                if (_cache.Count >= MaxCachedFiles && _lru.Last is { } oldest)
                {
                    _lru.RemoveLast();
                    _cache.Remove(oldest.Value.Path);
                }

                entry = new FileCacheEntry { Path = path, Stamp = stamp };
                node = new LinkedListNode<FileCacheEntry>(entry);
                _lru.AddFirst(node);
                _cache[path] = node;
            }

            MergeInterval(entry.Covered, start, end);
            MergeKeyframes(entry.Keyframes, times);
        }
    }

    private static bool TryResolveFromEntry(FileCacheEntry entry, double seconds, out double? keyframe)
    {
        double w1Start = Math.Max(0, seconds - Windows[0]);
        double w2Start = Math.Max(0, seconds - Windows[^1]);

        double? candidate = MaxKeyframeAtOrBefore(entry.Keyframes, seconds + Tolerance);
        if (candidate is { } k && k >= w2Start - Tolerance && IsCovered(entry.Covered, Math.Max(0, k), seconds))
        {
            if (k >= w1Start - Tolerance || IsCovered(entry.Covered, w1Start, seconds))
            {
                keyframe = k;
                return true;
            }
        }

        if (IsCovered(entry.Covered, w2Start, seconds))
        {
            keyframe = null;
            return true;
        }

        keyframe = null;
        return false;
    }

    private static double? MaxKeyframeAtOrBefore(List<double> sortedKeyframes, double maxTime)
    {
        if (sortedKeyframes.Count == 0)
        {
            return null;
        }

        int lo = 0;
        int hi = sortedKeyframes.Count - 1;
        int best = -1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            if (sortedKeyframes[mid] <= maxTime)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return best >= 0 ? sortedKeyframes[best] : null;
    }

    private static bool IsCovered(List<(double Start, double End)> covered, double from, double to)
    {
        foreach (var (s, e) in covered)
        {
            if (s <= from + IntervalEpsilon && e >= to - IntervalEpsilon)
            {
                return true;
            }
        }
        return false;
    }

    private static List<(double Start, double End)> ComputeUncoveredRanges(
        List<(double Start, double End)> covered, double start, double end)
    {
        var gaps = new List<(double Start, double End)>();
        double cursor = start;

        foreach (var (cs, ce) in covered)
        {
            if (ce <= cursor + IntervalEpsilon)
            {
                continue;
            }
            if (cs >= end - IntervalEpsilon)
            {
                break;
            }
            if (cs > cursor + IntervalEpsilon)
            {
                gaps.Add((cursor, cs));
            }
            cursor = Math.Max(cursor, ce);
            if (cursor >= end - IntervalEpsilon)
            {
                break;
            }
        }

        if (cursor < end - IntervalEpsilon)
        {
            gaps.Add((cursor, end));
        }

        return gaps;
    }

    private static void MergeInterval(List<(double Start, double End)> intervals, double start, double end)
    {
        if (end <= start)
        {
            return;
        }

        intervals.Add((start, end));
        intervals.Sort((a, b) => a.Start.CompareTo(b.Start));

        int write = 0;
        for (int read = 1; read < intervals.Count; read++)
        {
            var cur = intervals[write];
            var next = intervals[read];
            if (next.Start <= cur.End + IntervalEpsilon)
            {
                intervals[write] = (cur.Start, Math.Max(cur.End, next.End));
            }
            else
            {
                write++;
                intervals[write] = next;
            }
        }

        int removeCount = intervals.Count - (write + 1);
        if (removeCount > 0)
        {
            intervals.RemoveRange(write + 1, removeCount);
        }
    }

    private static void MergeKeyframes(List<double> target, List<double> additions)
    {
        if (additions.Count == 0)
        {
            return;
        }

        foreach (double t in additions)
        {
            if (double.IsFinite(t))
            {
                target.Add(t);
            }
        }

        target.Sort();
        if (target.Count <= 1)
        {
            return;
        }

        int write = 0;
        for (int read = 1; read < target.Count; read++)
        {
            if (target[read] - target[write] > KeyframeDedupeEpsilon)
            {
                write++;
                target[write] = target[read];
            }
        }

        int removeCount = target.Count - (write + 1);
        if (removeCount > 0)
        {
            target.RemoveRange(write + 1, removeCount);
        }
    }

    private static FileStamp? TryGetFileStamp(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    // null means the probe could not run; an empty list means it ran and saw no keyframe.
    private async Task<List<double>?> ProbeWindowAsync(string path, double start, double end, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(RunTimeout);
        try
        {
            if (!_noFfprobe)
            {
                try
                {
                    return await RunFfprobeAsync(path, start, end, timeout.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
                {
                    _noFfprobe = true;
                }
            }
            return await RunFfmpegAsync(path, start, end, timeout.Token).ConfigureAwait(false);
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

    private async Task<List<double>?> RunFfprobeAsync(string path, double start, double end, CancellationToken ct)
    {
        var parser = new PacketKeyframeParser();
        var outcome = await _runner.RunAsync(
            _ffprobePath,
            [
                "-v", "error", "-select_streams", "v:0",
                "-show_entries", "packet=pts_time,dts_time,flags",
                "-of", "default=noprint_wrappers=1",
                "-read_intervals", Invariant($"{start}%{end + Overshoot}"),
                "-i", path
            ],
            async reader =>
            {
                string? line;
                while ((line = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
                {
                    parser.Feed(line);
                }
                parser.Complete();
            },
            ct).ConfigureAwait(false);
        return outcome.ExitCode == 0 ? parser.Times : null;
    }

    private async Task<List<double>?> RunFfmpegAsync(string path, double start, double end, CancellationToken ct)
    {
        var outcome = await _runner.RunAsync(
            _ffmpegPath,
            [
                "-hide_banner", "-nostdin", "-skip_frame", "nokey",
                "-ss", Invariant($"{start}"), "-i", path, "-t", Invariant($"{end - start + Overshoot}"),
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
        string exe = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
        return string.IsNullOrEmpty(dir) ? exe : Path.Combine(dir, exe);
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"pts_time:\s*(-?[\d.]+)")]
    private static partial Regex PtsTime();

    private readonly record struct FileStamp(long Length, long LastWriteTimeTicks);

    private sealed class FileCacheEntry
    {
        public required string Path { get; init; }
        public FileStamp? Stamp { get; init; }
        public List<(double Start, double End)> Covered { get; } = [];
        public List<double> Keyframes { get; } = [];
    }

    internal sealed class PacketKeyframeParser
    {
        private bool _inBracketed;
        private bool _hasPts;
        private bool _hasDts;
        private bool _hasFlags;
        private double? _pts;
        private double? _dts;
        private string? _flags;

        public List<double> Times { get; } = [];

        public void Feed(string line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                if (!_inBracketed)
                {
                    Flush();
                }
                return;
            }

            if (trimmed == "[PACKET]")
            {
                Flush();
                _inBracketed = true;
                return;
            }

            if (trimmed == "[/PACKET]")
            {
                Flush();
                _inBracketed = false;
                return;
            }

            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
            {
                return;
            }

            string key = trimmed[..eq];
            string value = trimmed[(eq + 1)..];

            if (key == "pts_time")
            {
                if (!_inBracketed && (_hasPts || _hasFlags))
                {
                    Flush();
                }
                _hasPts = true;
                _pts = ParseTime(value);
            }
            else if (key == "dts_time")
            {
                if (!_inBracketed && (_hasDts || _hasFlags))
                {
                    Flush();
                }
                _hasDts = true;
                _dts = ParseTime(value);
            }
            else if (key == "flags")
            {
                if (!_inBracketed && _hasFlags)
                {
                    Flush();
                }
                _hasFlags = true;
                _flags = value;
            }
        }

        public void Complete() => Flush();

        private void Flush()
        {
            if (_hasFlags && _flags is not null && _flags.Contains('K', StringComparison.Ordinal))
            {
                if ((_pts ?? _dts) is { } time)
                {
                    Times.Add(time);
                }
            }

            _hasPts = false;
            _hasDts = false;
            _hasFlags = false;
            _pts = null;
            _dts = null;
            _flags = null;
        }
    }
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
    private long _version;

    public CutStartTracker(IKeyframeProbe probe, TimeSpan? debounce = null)
    {
        _probe = probe;
        _debounce = debounce ?? DefaultDebounce;
    }

    // Raised on a background thread (or synchronously when served from cache). Null means unknown or not applicable.
    public event Action<double?>? Changed;

    // Pass a null path or In point to stop tracking.
    public void Track(string? path, double? inPoint)
    {
        (string, double)? next = path is not null && inPoint is { } seconds ? (path, seconds) : null;
        if (next == _tracked) return;
        _tracked = next;

        long version = Interlocked.Increment(ref _version);
        _current?.Cancel();
        _current?.Dispose();
        _current = null;

        if (next is not var (p, s))
        {
            Changed?.Invoke(null);
            return;
        }

        if (_probe.TryGetCached(p, s, out double? cached))
        {
            Changed?.Invoke(cached);
            return;
        }

        Changed?.Invoke(null);
        var cts = new CancellationTokenSource();
        _current = cts;
        _ = RunAsync(p, s, version, cts.Token);
    }

    private async Task RunAsync(string path, double seconds, long version, CancellationToken ct)
    {
        try
        {
            await Task.Delay(_debounce, ct).ConfigureAwait(false);
            double? keyframe = await _probe.FindAtOrBeforeAsync(path, seconds, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _version) == version)
            {
                Changed?.Invoke(keyframe);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer In point.
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception)
        {
            if (Volatile.Read(ref _version) == version)
            {
                Changed?.Invoke(null);
            }
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _version);
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
