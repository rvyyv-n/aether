using System.Net;
using System.Net.NetworkInformation;
using Aether.Server.LanHost;
using Xunit;

namespace Aether.Server.Tests;

public class ServerTests
{
    [Fact]
    public void SessionToken_IsGeneratedAndNotEmpty()
    {
        var server = new KestrelStreamServer();
        Assert.NotNull(server.SessionToken);
        Assert.NotEmpty(server.SessionToken);
        Assert.Equal(16, server.SessionToken.Length); // 8 bytes hex = 16 chars
    }

    [Fact]
    public void GetMobileWatchUrl_ContainsPortAndToken()
    {
        var server = new KestrelStreamServer();
        string url = server.GetMobileWatchUrl();

        Assert.Contains($":{server.ActivePort}/watch?t={server.SessionToken}", url);
    }

    private static LanCandidate Nic(string name, string desc, NetworkInterfaceType type, string ip,
        bool up = true, bool gateway = true) =>
        new(name, desc, type, up, gateway, IPAddress.Parse(ip));

    [Fact]
    public void AddressPicker_SkipsVirtualAdaptersAndPrefersWifi()
    {
        var picked = LanAddressPicker.Pick(new[]
        {
            Nic("vEthernet (WSL)", "Hyper-V Virtual Ethernet Adapter", NetworkInterfaceType.Ethernet, "172.20.0.1", gateway: false),
            Nic("Loopback", "Software Loopback", NetworkInterfaceType.Loopback, "127.0.0.1"),
            Nic("Ethernet 2", "VirtualBox Host-Only Ethernet Adapter", NetworkInterfaceType.Ethernet, "192.168.56.1", gateway: false),
            Nic("Wi-Fi", "Intel Wi-Fi 6", NetworkInterfaceType.Wireless80211, "192.168.1.20"),
        });
        Assert.Equal(IPAddress.Parse("192.168.1.20"), picked);
    }

    [Fact]
    public void AddressPicker_IgnoresDownAdaptersAndLinkLocal()
    {
        var picked = LanAddressPicker.Pick(new[]
        {
            Nic("Ethernet", "Realtek", NetworkInterfaceType.Ethernet, "192.168.1.5", up: false),
            Nic("Wi-Fi", "Intel", NetworkInterfaceType.Wireless80211, "169.254.3.4"),
        });
        Assert.Null(picked);
    }

    [Fact]
    public void AddressPicker_PrefersPrivateRangeOverPublic()
    {
        var picked = LanAddressPicker.Pick(new[]
        {
            Nic("Ethernet", "Realtek", NetworkInterfaceType.Ethernet, "203.0.113.9"),
            Nic("Wi-Fi", "Intel", NetworkInterfaceType.Wireless80211, "10.0.0.7", gateway: false),
        });
        Assert.Equal(IPAddress.Parse("10.0.0.7"), picked);
    }

    private sealed class RunningServer : IAsyncDisposable
    {
        private readonly KestrelStreamServer _server = new(0, IPAddress.Loopback);
        public KestrelStreamServer Server => _server;
        private readonly string _file = Path.Combine(Path.GetTempPath(), $"aether-{Guid.NewGuid():N}.mp4");
        public HttpClient Http { get; } = new();
        public byte[] Bytes { get; } = Enumerable.Range(0, 1000).Select(i => (byte)(i % 251)).ToArray();
        public string Token => _server.SessionToken;
        public string Url(string path) => $"http://127.0.0.1:{_server.ActivePort}{path}";

        public static async Task<RunningServer> StartAsync(bool withFile = true)
        {
            var r = new RunningServer();
            if (withFile)
            {
                await File.WriteAllBytesAsync(r._file, r.Bytes);
                r._server.SetActiveFile(r._file);
            }
            await r._server.StartAsync();
            return r;
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await _server.DisposeAsync();
            File.Delete(_file);
        }
    }

    [Theory]
    [InlineData("/watch")]
    [InlineData("/stream/video")]
    public async Task MissingOrWrongToken_IsRejected(string path)
    {
        await using var s = await RunningServer.StartAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.GetAsync(s.Url(path))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.GetAsync(s.Url(path + "?t=nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await s.Http.GetAsync(s.Url(path + "?t="))).StatusCode);
    }

    [Fact]
    public async Task ValidToken_ServesWatchPageAndFullFile()
    {
        await using var s = await RunningServer.StartAsync();
        var page = await s.Http.GetAsync(s.Url($"/watch?t={s.Token}"));
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        string html = await page.Content.ReadAsStringAsync();
        Assert.Contains("canPlayType", html);
        Assert.DoesNotContain("http://", html.Replace("http://127", ""));

        var full = await s.Http.GetAsync(s.Url($"/stream/video?t={s.Token}"));
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        Assert.Equal(s.Bytes, await full.Content.ReadAsByteArrayAsync());
    }

    private static async Task<HttpResponseMessage> RangeAsync(RunningServer s, string range)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, s.Url($"/stream/video?t={s.Token}"));
        req.Headers.TryAddWithoutValidation("Range", range);
        return await s.Http.SendAsync(req);
    }

    [Theory]
    [InlineData("bytes=100-199", 100, 199)]
    [InlineData("bytes=900-", 900, 999)]
    [InlineData("bytes=-50", 950, 999)]
    public async Task Range_ReturnsPartialContent(string range, int from, int to)
    {
        await using var s = await RunningServer.StartAsync();
        var res = await RangeAsync(s, range);
        Assert.Equal(HttpStatusCode.PartialContent, res.StatusCode);
        Assert.Equal(s.Bytes[from..(to + 1)], await res.Content.ReadAsByteArrayAsync());
        Assert.Equal(from, res.Content.Headers.ContentRange!.From);
        Assert.Equal(to, res.Content.Headers.ContentRange!.To);
    }

    [Fact]
    public async Task Range_PastEndOfFile_IsNotSatisfiable()
    {
        await using var s = await RunningServer.StartAsync();
        var res = await RangeAsync(s, "bytes=5000-6000");
        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, res.StatusCode);
    }

    [Fact]
    public async Task Range_Malformed_FallsBackToWholeFile()
    {
        await using var s = await RunningServer.StartAsync();
        var res = await RangeAsync(s, "bytes=abc-def");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(s.Bytes, await res.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task NoActiveFile_StreamIsNotFound()
    {
        await using var s = await RunningServer.StartAsync(withFile: false);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Http.GetAsync(s.Url($"/stream/video?t={s.Token}"))).StatusCode);
    }

    [Fact]
    public async Task Watchers_CountWhileAPhoneStreams()
    {
        await using var s = await RunningServer.StartAsync();
        Assert.Equal(0, s.Server.WatcherCount);
        await s.Http.GetByteArrayAsync(s.Url($"/stream/video?t={s.Token}"));
        Assert.Equal(1, s.Server.WatcherCount); // still counted for a while after the request ends

        await s.Http.GetAsync(s.Url("/stream/video?t=nope"));
        await s.Http.GetAsync(s.Url($"/watch?t={s.Token}"));
        Assert.Equal(1, s.Server.WatcherCount); // only real video requests count, from one address
    }

    [Fact]
    public void WatcherTracker_KeepsAnOpenRequestAndForgetsAQuietOne()
    {
        var tracker = new WatcherTracker();
        var phone = IPAddress.Parse("192.168.1.30");
        var tablet = IPAddress.Parse("::ffff:192.168.1.31");
        long linger = (long)WatcherTracker.Linger.TotalMilliseconds;

        tracker.Begin(phone, 0);
        tracker.Begin(tablet, 0);
        tracker.End(tablet, 10);
        Assert.Equal(2, tracker.Count(10));

        Assert.Equal(1, tracker.Count(10 + linger + 1)); // the tablet went quiet; the phone's request is still open
        tracker.End(phone, 20 + linger);
        Assert.Equal(1, tracker.Count(20 + linger + 5));
        Assert.Equal(0, tracker.Count(20 + (2 * linger) + 1));

        tracker.Begin(IPAddress.Parse("192.168.1.31"), 0);
        tracker.Begin(tablet, 0); // the mapped form is the same device
        Assert.Equal(1, tracker.Count(0));
    }

    [Fact]
    public async Task SecondServerOnTheSamePort_ReportsThePortAsUnavailable()
    {
        await using var first = new KestrelStreamServer(0, IPAddress.Loopback);
        await first.StartAsync();
        await using var second = new KestrelStreamServer(first.ActivePort, IPAddress.Loopback);

        Exception error = await Assert.ThrowsAnyAsync<Exception>(() => second.StartAsync());
        Assert.True(KestrelStreamServer.IsPortUnavailable(error));
        Assert.False(KestrelStreamServer.IsPortUnavailable(new InvalidOperationException()));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(37)]
    [InlineData(5)]
    public void QrPng_UsesWholePixelsPerModuleWithinTheSize(int maxPixels)
    {
        (byte[] png, int pixels) = QrPairingService.CreatePngToFit("http://192.168.1.20:8443/watch?t=0123456789ABCDEF", maxPixels);

        int width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]; // the PNG header's width
        Assert.Equal(pixels, width);
        int modules = QrPairingService.CreatePngToFit("http://192.168.1.20:8443/watch?t=0123456789ABCDEF", 1).Pixels;
        Assert.Equal(0, pixels % modules);
        Assert.True(pixels <= Math.Max(maxPixels, modules));
        Assert.True(pixels + modules > maxPixels || pixels == modules);
    }
}
