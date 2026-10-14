using System.Diagnostics;

namespace Aether.Core.Playback.Streams;

public sealed record ProcessRunResult(int ExitCode, string StandardOutput, string StandardError);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct);
}

public sealed class DefaultProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(startInfo);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(string.Concat("Failed to start process: ", startInfo.FileName));
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new StreamResolutionException(StreamFailureReason.YtDlpMissing, "yt-dlp executable was not found.", inner: ex);
        }

        try
        {
            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(ct);

            await Task.WhenAll(stdoutTask, stderrTask, process.WaitForExitAsync(ct)).ConfigureAwait(false);

            return new ProcessRunResult(process.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception)
            {
                // Process may have already exited
            }
            throw;
        }
    }
}
