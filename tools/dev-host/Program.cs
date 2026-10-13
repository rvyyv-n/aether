using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aether.Server.LanHost;

namespace Aether.DevHost;

public static class Program
{
    public static async Task Main(string[] args)
    {
        string? videoPath = args.Length > 0 ? Path.GetFullPath(args[0]) : null;

        var server = new KestrelStreamServer();

        if (!string.IsNullOrEmpty(videoPath))
        {
            if (File.Exists(videoPath))
            {
                server.SetActiveFile(videoPath);
                Console.WriteLine($"[Aether Dev Host] Serving video: {videoPath}");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[Aether Dev Host] Warning: File not found: {videoPath}");
                Console.ResetColor();
            }
        }
        else
        {
            Console.WriteLine("[Aether Dev Host] No video specified. You can pass a video path as an argument to stream it.");
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };

        await server.StartAsync(cts.Token);

        string localUrl = $"http://localhost:{server.ActivePort}/watch?t={server.SessionToken}";
        string mobileUrl = server.GetMobileWatchUrl();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n=== Aether Local Web Host Running ===");
        Console.ResetColor();
        Console.WriteLine($"  Local player URL  : {localUrl}");
        Console.WriteLine($"  Mobile/LAN URL    : {mobileUrl}");
        Console.WriteLine($"  Session Token     : {server.SessionToken}");
        Console.WriteLine($"  Active Port       : {server.ActivePort}");
        Console.WriteLine("\nPress Ctrl+C to stop the host.\n");

        try
        {
            await Task.Delay(-1, cts.Token);
        }
        catch (TaskCanceledException)
        {
            Console.WriteLine("[Aether Dev Host] Shutting down...");
        }
        finally
        {
            await server.DisposeAsync();
        }
    }
}
