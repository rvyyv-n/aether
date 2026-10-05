using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Aether.PvP.Review.Clipping;

public record ClipRequest(
    string SourceFilePath,
    double StartTimeSeconds,
    double EndTimeSeconds,
    string DestinationDirectory,
    string ClipNamePrefix = "Aether_PvP_Clip"
);

public static class LosslessClipper
{
    public static async Task<string> ExportLosslessAsync(
        ClipRequest request, string ffmpegPath = "ffmpeg.exe", CancellationToken cancellationToken = default)
    {
        if (!File.Exists(request.SourceFilePath))
            throw new FileNotFoundException("Source video file not found", request.SourceFilePath);

        if (request.EndTimeSeconds <= request.StartTimeSeconds)
            throw new ArgumentException("End timestamp must be strictly greater than start timestamp.");

        Directory.CreateDirectory(request.DestinationDirectory);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string extension = Path.GetExtension(request.SourceFilePath);
        string outputPath = Path.Combine(request.DestinationDirectory, $"{request.ClipNamePrefix}_{timestamp}{extension}");
        string tempPath = outputPath + ".part";

        string startStr = request.StartTimeSeconds.ToString("F3", CultureInfo.InvariantCulture);
        string durationStr = (request.EndTimeSeconds - request.StartTimeSeconds).ToString("F3", CultureInfo.InvariantCulture);

        // FFmpeg stream-copy: zero re-encoding, preserving native 240/360fps streams
        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true
        };
        foreach (var a in new[] { "-hide_banner", "-nostdin", "-ss", startStr, "-i", request.SourceFilePath,
                                  "-t", durationStr, "-map", "0", "-c", "copy",
                                  "-avoid_negative_ts", "make_zero", "-f", MuxerFor(extension), "-y", tempPath })
            startInfo.ArgumentList.Add(a);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
            TryDelete(tempPath);
            throw;
        }

        if (process.ExitCode != 0)
        {
            TryDelete(tempPath);
            throw new InvalidOperationException($"FFmpeg stream copy failed with exit code {process.ExitCode}: {await stderr}");
        }

        File.Move(tempPath, outputPath, overwrite: true);
        return outputPath;
    }

    private static string MuxerFor(string ext) => ext.ToLowerInvariant() switch
    {
        ".mkv" => "matroska",
        ".webm" => "webm",
        ".mov" => "mov",
        _ => "mp4"
    };

    private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
}
