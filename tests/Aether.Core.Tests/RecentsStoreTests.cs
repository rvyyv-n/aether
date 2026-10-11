using Aether.Core.Playback.Recents;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class RecentsStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 18, 0, 0, TimeSpan.FromHours(1));

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public bool Fail { get; set; }
        public int Writes { get; private set; }
        public string? Read() => Fail ? throw new IOException() : Content;
        public void Write(string content)
        {
            if (Fail)
            {
                throw new IOException();
            }
            Content = content;
            Writes++;
        }
    }

    private static RecentsStore NewStore(FakeStorage storage)
    {
        var store = new RecentsStore(storage);
        store.Load();
        return store;
    }

    [Fact]
    public void Empty_UntilSomethingOpens()
    {
        Assert.Empty(NewStore(new FakeStorage()).Items);
    }

    [Fact]
    public void Add_KeepsTheNewestThree_NewestFirst()
    {
        var store = NewStore(new FakeStorage());
        store.Add(@"D:\a.mp4", "a.mp4", Now);
        store.Add(@"D:\b.mp4", "b.mp4", Now);
        store.Add(@"D:\c.mp4", "c.mp4", Now);
        store.Add(@"D:\d.mp4", "d.mp4", Now);

        Assert.Equal([@"D:\d.mp4", @"D:\c.mp4", @"D:\b.mp4"], store.Items.Select(i => i.Location));
    }

    [Fact]
    public void Reopening_MovesToTheTopWithoutDuplicating_AndKeepsTheDuration()
    {
        var store = NewStore(new FakeStorage());
        store.Add(@"D:\a.mp4", "a.mp4", Now);
        store.SetDuration(@"D:\a.mp4", 195);
        store.Add(@"D:\b.mp4", "b.mp4", Now);

        store.Add(@"d:\A.MP4", "A.MP4", Now.AddHours(1));

        Assert.Equal(2, store.Items.Count);
        Assert.Equal(@"d:\A.MP4", store.Items[0].Location);
        Assert.Equal(195, store.Items[0].Duration);
    }

    [Fact]
    public void Changes_RoundTripThroughStorage()
    {
        var storage = new FakeStorage();
        var store = NewStore(storage);
        store.Add(@"D:\Captures\clip-01.mp4", "clip-01.mp4", Now);
        store.SetDuration(@"D:\Captures\clip-01.mp4", 195.5);

        var reloaded = NewStore(storage);

        RecentItem item = Assert.Single(reloaded.Items);
        Assert.Equal(new RecentItem(@"D:\Captures\clip-01.mp4", "clip-01.mp4", Now, 195.5), item);
    }

    [Fact]
    public void Remove_And_Clear_Persist()
    {
        var storage = new FakeStorage();
        var store = NewStore(storage);
        store.Add(@"D:\a.mp4", "a.mp4", Now);
        store.Add(@"D:\b.mp4", "b.mp4", Now);

        store.Remove(@"D:\a.mp4");
        Assert.Equal([@"D:\b.mp4"], NewStore(storage).Items.Select(i => i.Location));

        store.Clear();
        Assert.Empty(NewStore(storage).Items);
    }

    [Fact]
    public void NoChange_DoesNotWriteOrRaise()
    {
        var storage = new FakeStorage();
        var store = NewStore(storage);
        int raised = 0;
        store.Changed += (_, _) => raised++;

        store.Remove(@"D:\missing.mp4");
        store.Clear();

        Assert.Equal(0, storage.Writes);
        Assert.Equal(0, raised);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"items\": 4}")]
    public void CorruptFile_GivesAnEmptyList(string json)
    {
        Assert.Empty(NewStore(new FakeStorage { Content = json }).Items);
    }

    [Fact]
    public void Parse_SkipsBadEntries_DuplicatesAndAnythingPastThree()
    {
        const string json = """
            {"items": [
              {"location": "D:\\a.mp4", "opened": "2026-10-11T10:00:00+01:00"},
              {"location": "", "opened": "2026-10-11T10:00:00+01:00"},
              {"location": "D:\\b.mp4"},
              {"location": "D:\\A.mp4", "opened": "2026-10-11T10:00:00+01:00"},
              {"location": "D:\\c.mp4", "opened": "2026-10-10T10:00:00+01:00", "duration": -3},
              {"location": "D:\\d.mp4", "opened": "2026-10-09T10:00:00+01:00"},
              {"location": "D:\\e.mp4", "opened": "2026-10-08T10:00:00+01:00"}
            ]}
            """;

        RecentItem[] items = RecentsStore.Parse(json);

        Assert.Equal([@"D:\a.mp4", @"D:\c.mp4", @"D:\d.mp4"], items.Select(i => i.Location));
        Assert.Equal("a.mp4", items[0].DisplayName); // a missing name falls back to the file name
        Assert.Equal(0, items[1].Duration);
    }

    [Fact]
    public void StorageFailures_NeverThrow_AndTheListStaysInMemory()
    {
        var storage = new FakeStorage { Fail = true };
        var store = NewStore(storage);

        store.Add(@"D:\a.mp4", "a.mp4", Now);

        Assert.Single(store.Items);
    }

    [Theory]
    [InlineData(0, RecentDay.Today)]
    [InlineData(-17, RecentDay.Today)] // 01:00 the same day
    [InlineData(-19, RecentDay.Yesterday)] // 23:00 the day before
    [InlineData(-48, RecentDay.Earlier)]
    public void Day_ComparesCalendarDays(int hours, RecentDay expected) =>
        Assert.Equal(expected, RecentDisplay.Day(Now.AddHours(hours), Now));

    [Fact]
    public void Day_UsesTheViewersOffset()
    {
        // 23:30 UTC on the 10th is 00:30 on the 11th at UTC+1.
        var openedUtc = new DateTimeOffset(2026, 10, 10, 23, 30, 0, TimeSpan.Zero);
        Assert.Equal(RecentDay.Today, RecentDisplay.Day(openedUtc, Now));
    }

    [Fact]
    public void Source_IsTheFolderForFilesAndTheSiteForLinks()
    {
        Assert.Equal(@"D:\Captures", RecentDisplay.Source(new RecentItem(@"D:\Captures\clip-01.mp4", "clip-01.mp4", Now)));
        Assert.Equal("youtube.com", RecentDisplay.Source(new RecentItem("https://www.youtube.com/watch?v=1", "Sunset", Now)));
    }

    [Fact]
    public void IsMissing_OnlyForFilesThatAreGone()
    {
        var file = new RecentItem(@"D:\gone.mkv", "gone.mkv", Now);
        var link = new RecentItem("https://youtu.be/x", "Sunset", Now);

        Assert.True(RecentDisplay.IsMissing(file, _ => false));
        Assert.False(RecentDisplay.IsMissing(file, _ => true));
        Assert.False(RecentDisplay.IsMissing(link, _ => false));
    }
}
