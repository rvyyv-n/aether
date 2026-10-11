using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Aether.PvP.Review.Clipping;

public sealed record ClipResult(string OutputPath, long SizeBytes, TimeSpan Elapsed);

public sealed class ClipExportService : IDisposable
{
    private static readonly TimeSpan PreWarmTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _lifetime = new();
    private volatile string _ffmpegPath;
    private volatile bool _isFfmpegAvailable;

    public ClipExportService(string ffmpegPath = "ffmpeg.exe")
    {
        _ffmpegPath = ffmpegPath;
    }

    public bool IsFfmpegAvailable => _isFfmpegAvailable;

    // Running the binary once pulls it into the OS file cache so the first real export doesn't
    // pay for a cold disk read or the first-run security scan.
    public async Task PreWarmAsync(string ffmpegPath = "ffmpeg.exe", CancellationToken ct = default)
    {
        _ffmpegPath = ffmpegPath;

        var startInfo = new ProcessStartInfo(ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("-version");

        Process? process;
        try
        {
            process = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            process = null;
        }

        if (process is null)
        {
            _isFfmpegAvailable = false;
            return;
        }

        using (process)
        {
            Task output = process.StandardOutput.ReadToEndAsync();
            Task errors = process.StandardError.ReadToEndAsync();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            linked.CancelAfter(PreWarmTimeout);
            try
            {
                await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
                await Task.WhenAll(output, errors).ConfigureAwait(false);
                _isFfmpegAvailable = process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                Kill(process);
                _isFfmpegAvailable = false;
                if (ct.IsCancellationRequested) throw;
            }
        }
    }

    public async Task<ClipResult> ExportAsync(ClipRequest request, CancellationToken ct = default)
    {
        Validate(request);
        Directory.CreateDirectory(request.DestinationDirectory);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
        var token = linked.Token;

        var stopwatch = Stopwatch.StartNew();
        string outputPath = ReserveOutputPath(request);
        string tempPath = outputPath + ".part";

        var startInfo = new ProcessStartInfo(_ffmpegPath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in FfmpegArguments.Build(request, tempPath))
            startInfo.ArgumentList.Add(a);

        bool committed = false;
        try
        {
            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            }
            catch (Win32Exception ex)
            {
                _isFfmpegAvailable = false;
                throw new FileNotFoundException($"FFmpeg could not be started from '{_ffmpegPath}'.", _ffmpegPath, ex);
            }

            using (process)
            {
                Task drainOutput = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();

                try
                {
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Kill(process);
                    // The .part can't be deleted while ffmpeg still holds it open.
                    await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                await drainOutput.ConfigureAwait(false);
                string errors = await stderr.ConfigureAwait(false);
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"FFmpeg stream copy failed with exit code {process.ExitCode}: {errors}");
            }

            if (!File.Exists(tempPath))
                throw new InvalidOperationException("FFmpeg finished without writing an output file.");

            // The final name was chosen before ffmpeg ran, so a clip made in the same second
            // gets its own name and the rename never overwrites an earlier export.
            File.Move(tempPath, outputPath, overwrite: false);
            committed = true;

            stopwatch.Stop();
            return new ClipResult(outputPath, new FileInfo(outputPath).Length, stopwatch.Elapsed);
        }
        finally
        {
            if (!committed) TryDelete(tempPath);
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }

    internal static void Validate(ClipRequest request)
    {
        if (!File.Exists(request.SourceFilePath))
            throw new FileNotFoundException("Source video file not found", request.SourceFilePath);

        if (request.StartTimeSeconds < 0)
            throw new ArgumentException("Start timestamp must not be negative.");

        if (request.EndTimeSeconds <= request.StartTimeSeconds)
            throw new ArgumentException("End timestamp must be strictly greater than start timestamp.");
    }

    internal static string ReserveOutputPath(ClipRequest request)
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string extension = Path.GetExtension(request.SourceFilePath);
        string baseName = $"{request.ClipNamePrefix}_{timestamp}";

        string candidate = Path.Combine(request.DestinationDirectory, baseName + extension);
        for (int i = 1; File.Exists(candidate) || File.Exists(candidate + ".part"); i++)
            candidate = Path.Combine(request.DestinationDirectory, $"{baseName}_{i}{extension}");
        return candidate;
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
