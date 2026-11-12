using System.Text.RegularExpressions;
using Aether.Core.Tests.TestSupport;

namespace Aether.Core.Tests;

public sealed class UpdateFeedTests
{
    [Fact]
    public void InstalledCopiesLookForUpdatesInTheRealRepository()
    {
        string source = File.ReadAllText(RepoPath.Get("src", "Aether.App", "Updates", "UpdateFeed.cs"));
        Match url = Regex.Match(source, @"RepositoryUrl\s*=\s*""([^""]*)""");

        Assert.True(url.Success, "UpdateFeed.RepositoryUrl not found");
        Assert.Equal("https://github.com/rvyyv-n/aether", url.Groups[1].Value);
    }
}
