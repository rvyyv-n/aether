using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace Aether.Server.LanHost;

public sealed class KestrelStreamServer : IAsyncDisposable
{
    private WebApplication? _app;
    private string? _currentlyHostedFilePath;
    public int ActivePort { get; private set; } = 8443;
    public string SessionToken { get; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));

    public void SetActiveFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            _currentlyHostedFilePath = filePath;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Any, ActivePort));
        _app = builder.Build();

        // HTTP 206 Partial Content Stream
        _app.MapGet("/stream/video", (string t) =>
        {
            if (t != SessionToken) return Results.Unauthorized();
            if (string.IsNullOrEmpty(_currentlyHostedFilePath) || !File.Exists(_currentlyHostedFilePath))
                return Results.NotFound();
            return Results.File(_currentlyHostedFilePath, ContentTypeFor(_currentlyHostedFilePath),
                                enableRangeProcessing: true);
        });

        // Mobile Video Viewer Shell
        _app.MapGet("/watch", (string t) =>
        {
            if (t != SessionToken) return Results.Unauthorized();
            string html = $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1">
                    <title>Aether LAN Player</title>
                    <style>
                        html, body { margin: 0; height: 100%; background: #000; }
                        video { width: 100%; height: 100%; object-fit: contain; }
                    </style>
                </head>
                <body>
                    <video src="/stream/video?t={{t}}" controls autoplay playsinline></video>
                </body>
                </html>
                """;
            return Results.Content(html, "text/html");
        });

        await _app.StartAsync(cancellationToken);
        if (ActivePort == 0 && _app.Urls.Any())
        {
            ActivePort = new Uri(_app.Urls.First()).Port;
        }
    }

    public string GetLocalIpAddress()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("10.255.255.255", 1);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString() ?? "127.0.0.1";
        }
        catch
        {
            return "127.0.0.1";
        }
    }

    public string GetMobileWatchUrl() => $"http://{GetLocalIpAddress()}:{ActivePort}/watch?t={SessionToken}";

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        _ => "application/octet-stream"
    };

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
