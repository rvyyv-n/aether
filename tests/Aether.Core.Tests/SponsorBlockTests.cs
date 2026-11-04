using System.Net;
using System.Text;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;

namespace Aether.Core.Tests;

public class SponsorBlockTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://youtube.com/watch?feature=share&v=dQw4w9WgXcQ&t=42", "dQw4w9WgXcQ")]
    [InlineData("https://m.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://music.youtube.com/watch?v=dQw4w9WgXcQ&list=x", "dQw4w9WgXcQ")]
    [InlineData("https://youtu.be/dQw4w9WgXcQ?si=abc", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/shorts/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube.com/live/dQw4w9WgXcQ?feature=share", "dQw4w9WgXcQ")]
    [InlineData("https://www.youtube-nocookie.com/embed/dQw4w9WgXcQ", "dQw4w9WgXcQ")]
    public void YouTube_Links_Give_Their_Video_Id(string link, string id) => Assert.Equal(id, SponsorBlock.VideoId(link));

    [Theory]
    [InlineData("https://vimeo.com/123456")]
    [InlineData("https://www.youtube.com/watch?v=short")]
    [InlineData("https://www.youtube.com/playlist?list=PL123")]
    [InlineData("https://evil.example/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://notyoutube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData(@"C:\videos\dQw4w9WgXcQ.mp4")]
    [InlineData(null)]
    public void Anything_Else_Has_None(string? link) => Assert.Null(SponsorBlock.VideoId(link));

    [Fact]
    public void The_Request_Names_Only_A_Hash_Prefix_Of_The_Video_And_The_Categories()
    {
        Uri uri = SponsorBlock.RequestUri("dQw4w9WgXcQ", [SponsorCategory.Sponsor, SponsorCategory.Intro]);

        Assert.Equal("sponsor.ajay.app", uri.Host);
        Assert.Equal("/api/skipSegments/" + SponsorBlock.HashPrefix("dQw4w9WgXcQ"), uri.AbsolutePath);
        Assert.DoesNotContain("dQw4w9WgXcQ", uri.ToString(), StringComparison.Ordinal);
        Assert.Contains("categories=%5B%22sponsor%22%2C%22intro%22%5D", uri.Query, StringComparison.Ordinal);
        Assert.Contains("actionType=skip", uri.Query, StringComparison.Ordinal);
        Assert.Equal(4, SponsorBlock.HashPrefix("dQw4w9WgXcQ").Length);
        Assert.Equal("ba78", SponsorBlock.HashPrefix("abc"));
    }

    private const string Reply = """
        [
          {"videoID":"otherVideo1","segments":[{"category":"sponsor","actionType":"skip","segment":[1,2]}]},
          {"videoID":"dQw4w9WgXcQ","segments":[
            {"category":"intro","actionType":"skip","segment":[0,7.5],"UUID":"a"},
            {"category":"sponsor","actionType":"skip","segment":[30.25,61],"UUID":"b"},
            {"category":"selfpromo","actionType":"mute","segment":[70,80],"UUID":"c"},
            {"category":"poi_highlight","actionType":"poi","segment":[90,90],"UUID":"d"},
            {"category":"interaction","actionType":"skip","segment":[100,99],"UUID":"e"},
            {"category":"outro","actionType":"skip","segment":[200,212],"UUID":"f"}
          ]}
        ]
        """;

    [Fact]
    public void A_Reply_Gives_This_Video_s_Skip_Segments_In_Order_And_Drops_The_Rest()
    {
        IReadOnlyList<SponsorSegment> segments = SponsorBlock.Parse(Reply, "dQw4w9WgXcQ");

        Assert.Equal(
            [
                new SponsorSegment(0, 7.5, SponsorCategory.Intro),
                new SponsorSegment(30.25, 61, SponsorCategory.Sponsor),
                new SponsorSegment(200, 212, SponsorCategory.Outro),
            ],
            segments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""[{"videoID":"dQw4w9WgXcQ","segments":[{"category":"sponsor","segment":["a"]}]}]""")]
    public void A_Broken_Reply_Gives_No_Segments(string json) => Assert.Empty(SponsorBlock.Parse(json, "dQw4w9WgXcQ"));

    private sealed class Server(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? Asked { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    [Fact]
    public async Task Fetching_Asks_The_Prefix_Url_And_Reads_The_Reply()
    {
        var server = new Server(HttpStatusCode.OK, Reply);
        using var http = new HttpClient(server);

        IReadOnlyList<SponsorSegment> segments = await SponsorBlock.FetchAsync(http, "dQw4w9WgXcQ", [SponsorCategory.Sponsor], CancellationToken.None);

        Assert.Equal(3, segments.Count);
        Assert.Equal(SponsorBlock.RequestUri("dQw4w9WgXcQ", [SponsorCategory.Sponsor]), server.Asked);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task No_Segments_Or_A_Server_Error_Gives_None(HttpStatusCode status)
    {
        using var http = new HttpClient(new Server(status, "Not Found"));

        Assert.Empty(await SponsorBlock.FetchAsync(http, "dQw4w9WgXcQ", [SponsorCategory.Sponsor], CancellationToken.None));
    }

    [Fact]
    public void Categories_Are_Saved_As_Their_SponsorBlock_Names()
    {
        Assert.Equal([SponsorCategory.Sponsor, SponsorCategory.Outro], SponsorBlock.ParseCategories("sponsor, outro,bogus"));
        Assert.Equal("sponsor,selfpromo", SponsorBlock.FormatCategories([SponsorCategory.Sponsor, SponsorCategory.SelfPromo]));
    }

    [Fact]
    public void It_Is_Off_By_Default_And_Skips_Sponsors_Self_Promotion_And_Reminders()
    {
        var settings = new AetherSettings();

        Assert.False(settings.SponsorBlock);
        Assert.True(settings.SponsorBlockAutoSkip);
        Assert.Equal(
            [SponsorCategory.Sponsor, SponsorCategory.SelfPromo, SponsorCategory.Interaction],
            SponsorBlock.ParseCategories(settings.SponsorBlockCategories));
    }
}
