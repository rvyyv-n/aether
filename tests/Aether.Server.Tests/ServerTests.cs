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
}
