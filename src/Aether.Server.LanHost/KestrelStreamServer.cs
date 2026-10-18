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
    private readonly IPAddress _bindAddress;
    public int ActivePort { get; private set; }

    public KestrelStreamServer(int port = 8443, IPAddress? bindAddress = null)
    {
        ActivePort = port;
        _bindAddress = bindAddress ?? IPAddress.Any;
    }

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
        builder.WebHost.UseKestrel(options => options.Listen(_bindAddress, ActivePort));
        _app = builder.Build();

        // HTTP 206 Partial Content Stream
        _app.MapGet("/stream/video", (string? t) =>
        {
            if (!IsValidToken(t)) return Results.Unauthorized();
            if (string.IsNullOrEmpty(_currentlyHostedFilePath) || !File.Exists(_currentlyHostedFilePath))
                return Results.NotFound();
            return Results.File(_currentlyHostedFilePath, ContentTypeFor(_currentlyHostedFilePath),
                                enableRangeProcessing: true);
        });

        // The browser tab icon; made by tools/make_app_icon.py, follows the phone's light or dark setting.
        _app.MapGet("/favicon.svg", () =>
            Results.Stream(typeof(KestrelStreamServer).Assembly.GetManifestResourceStream("favicon.svg")!, "image/svg+xml"));

        // Mobile Video Viewer Shell
        _app.MapGet("/watch", (string? t) =>
        {
            if (!IsValidToken(t)) return Results.Unauthorized();
            string mime = _currentlyHostedFilePath is null ? "video/mp4" : ContentTypeFor(_currentlyHostedFilePath);
            return Results.Content(WatchPage.Build(t!, mime), "text/html");
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
            var preferred = LanAddressPicker.PickFromSystem();
            if (preferred is not null) return preferred.ToString();

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

    private static readonly System.Text.Encoding Utf8 = System.Text.Encoding.UTF8;

    private bool IsValidToken(string? t) =>
        t is not null && CryptographicOperations.FixedTimeEquals(Utf8.GetBytes(t), Utf8.GetBytes(SessionToken));

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
