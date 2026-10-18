using System.ComponentModel;
using System.Diagnostics;

namespace Aether.PvP.Review.Clipping;

public sealed record ProcessOutcome(int ExitCode, string StandardError);

// Everything that launches ffmpeg for the re-encode path goes through here so tests can fake it.
public interface IProcessRunner
{
    // stdoutConsumer reads standard output until it ends. Standard error is always collected.
    // Cancelling kills the whole process tree and rethrows OperationCanceledException once it has exited.
    Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<TextReader, Task>? stdoutConsumer,
        CancellationToken ct);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    public async Task<ProcessOutcome> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        Func<TextReader, Task>? stdoutConsumer,
        CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var a in arguments)
            startInfo.ArgumentList.Add(a);

        Process process;
        try
        {
            process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start ffmpeg.");
        }
        catch (Win32Exception ex)
        {
            throw new FileNotFoundException($"FFmpeg could not be started from '{fileName}'.", fileName, ex);
        }

        using (process)
        {
            // Both pipes are read from the start: a full pipe blocks ffmpeg and the exit never comes.
            Task stdout = DrainAsync(process.StandardOutput, stdoutConsumer);
            Task<string> stderr = process.StandardError.ReadToEndAsync();

            try
            {
                await process.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { /* already exited */ }
                // The .part can't be deleted while ffmpeg still holds it open.
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            await stdout.ConfigureAwait(false);
            return new ProcessOutcome(process.ExitCode, await stderr.ConfigureAwait(false));
        }
    }

    private static async Task DrainAsync(StreamReader reader, Func<TextReader, Task>? consumer)
    {
        if (consumer is not null)
            await consumer(reader).ConfigureAwait(false);
        // A consumer that stops early must not leave the pipe unread.
        await reader.ReadToEndAsync().ConfigureAwait(false);
    }
}
