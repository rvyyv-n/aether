using System.Text;
using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public sealed class YtDlpDownloadCheckTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void ExpectedHash_FindsTheLineForTheNamedFile()
    {
        string sums = $"{new string('a', 64)}  yt-dlp\n{Hash}  yt-dlp.exe\n{new string('b', 64)}  yt-dlp_x86.exe\n";

        Assert.Equal(Hash, YtDlpDownloadCheck.ExpectedHash(sums, "yt-dlp.exe"));
    }

    [Fact]
    public void ExpectedHash_HandlesWindowsLineEndingsBinaryMarkerAndUppercase()
    {
        string sums = $"{Hash.ToUpperInvariant()} *yt-dlp.exe\r\n";

        Assert.Equal(Hash, YtDlpDownloadCheck.ExpectedHash(sums, "yt-dlp.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a sums file")]
    [InlineData("abc123  yt-dlp.exe")]
    public void ExpectedHash_IsNullWhenThereIsNoUsableLine(string sums)
    {
        Assert.Null(YtDlpDownloadCheck.ExpectedHash(sums, "yt-dlp.exe"));
    }

    [Fact]
    public async Task HashAsync_MatchesTheKnownSha256()
    {
        using var content = new MemoryStream(Encoding.ASCII.GetBytes("abc"));

        string hash = await YtDlpDownloadCheck.HashAsync(content);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", hash);
    }

    [Fact]
    public void Matches_IgnoresCaseAndRejectsADifferentOrMissingHash()
    {
        Assert.True(YtDlpDownloadCheck.Matches(Hash, Hash.ToUpperInvariant()));
        Assert.False(YtDlpDownloadCheck.Matches(Hash, new string('f', 64)));
        Assert.False(YtDlpDownloadCheck.Matches(null, Hash));
    }
}
