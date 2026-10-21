using System.Diagnostics;
using System.Globalization;
using System.Runtime;

namespace Aether_App;

/// <summary>
/// Startup and playback timings for <c>tools/measure-performance.ps1</c>. Off unless the <c>AETHER_PERF_LOG</c>
/// environment variable names a file; then each milestone appends one <c>key=value</c> line to it. Nothing here runs
/// on a frame path: a few lines per launch.
/// </summary>
internal static class PerfLog
{
    private static readonly string? s_path = Environment.GetEnvironmentVariable("AETHER_PERF_LOG");
    private static readonly Lock s_gate = new();
    private static int s_windowShown;

    public static bool IsEnabled => s_path is not null;

    /// <summary>Milliseconds since the process started, so time spent before Main (runtime start-up) is counted too.</summary>
    private static double SinceProcessStart()
    {
        using Process process = Process.GetCurrentProcess();
        return (DateTime.Now - process.StartTime).TotalMilliseconds;
    }

    /// <summary>Records how long the process took to reach <paramref name="milestone"/>.</summary>
    public static void Mark(string milestone)
    {
        if (s_path is not null)
        {
            Write($"{milestone}={SinceProcessStart().ToString("F0", CultureInfo.InvariantCulture)}");
        }
    }

    /// <summary>The first frame the window draws: the moment the user sees it.</summary>
    public static void MarkWindowShown()
    {
        if (s_path is not null && Interlocked.Exchange(ref s_windowShown, 1) == 0)
        {
            Mark("window_shown_ms");
        }
    }

    /// <summary>The garbage collector's actual mode, as the published build runs it.</summary>
    public static void WriteGcSettings()
    {
        if (s_path is not null)
        {
            Write($"gc_server={GCSettings.IsServerGC}");
            Write($"gc_latency_mode={GCSettings.LatencyMode}");
        }
    }

    /// <summary>Playback and collector totals for the session, written as the window closes.</summary>
    public static void WriteSessionTotals(long droppedFrames, long decoderDroppedFrames, long framesPresented, double displayFps)
    {
        if (s_path is null)
        {
            return;
        }
        Write($"dropped_frames={droppedFrames}");
        Write($"decoder_dropped_frames={decoderDroppedFrames}");
        Write($"frames_presented={framesPresented}");
        Write($"display_fps={displayFps.ToString("F2", CultureInfo.InvariantCulture)}");
        Write($"gc_gen0={GC.CollectionCount(0)}");
        Write($"gc_gen1={GC.CollectionCount(1)}");
        Write($"gc_gen2={GC.CollectionCount(2)}");
        Write($"gc_pause_total_ms={GC.GetTotalPauseDuration().TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture)}");
    }

    private static void Write(string line)
    {
        try
        {
            lock (s_gate)
            {
                File.AppendAllText(s_path!, line + Environment.NewLine);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A measurement aid; a locked or missing folder must not affect the app.
        }
    }
}
