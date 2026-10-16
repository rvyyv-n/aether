using Aether.Core.Playback.Streams;
using Xunit;

namespace Aether.Core.Tests;

public sealed class MpvLoadOptionsTests
{
    [Fact]
    public void ForStream_WithoutAudio_IsEmpty()
    {
        Assert.Equal(string.Empty, MpvLoadOptions.ForStream(null));
        Assert.Equal(string.Empty, MpvLoadOptions.ForStream(""));
    }

    [Fact]
    public void ForStream_QuotesTheAudioUrlByByteLength()
    {
        const string url = "https://r1.googlevideo.com/videoplayback?a=1,2&b=c;d";
        Assert.Equal($"audio-files-append=%{url.Length}%{url}", MpvLoadOptions.ForStream(url));
    }

    [Fact]
    public void Build_CountsUtf8Bytes_AndJoinsWithCommas()
    {
        string options = MpvLoadOptions.Build([new("a", "é"), new("b", "x,y")]);
        Assert.Equal("a=%2%é,b=%3%x,y", options);
    }
}
