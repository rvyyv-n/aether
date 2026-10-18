using Aether.Core.Playback.Resume;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Tests;

public class ResumeStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 18, 0, 0, TimeSpan.Zero);

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public bool Fail { get; set; }
        public string? Read() => Fail ? throw new IOException() : Content;
        public void Write(string content) => Content = Fail ? throw new IOException() : content;
    }

    [Fact]
    public void Saved_Position_Round_Trips_Through_Storage()
    {
        var storage = new FakeStorage();
        new ResumeStore(storage).Save(@"C:\v\a.mp4", 123.5, 600, Now);

        var reloaded = new ResumeStore(storage);
        reloaded.Load();

        Assert.Equal(123.5, reloaded.TryGet(@"c:\V\A.MP4"));
    }

    [Theory]
    [InlineData(4.9, 600)]
    [InlineData(0, 600)]
    [InlineData(590.1, 600)]
    [InlineData(600, 600)]
    [InlineData(30, 0)]
    [InlineData(8, 14)]
    [InlineData(double.NaN, 600)]
    public void Positions_Not_Worth_Resuming_Are_Not_Kept(double position, double duration)
    {
        var store = new ResumeStore(new FakeStorage());

        store.Save("a.mp4", position, duration, Now);

        Assert.Null(store.TryGet("a.mp4"));
        Assert.Empty(store.Entries);
    }

    [Theory]
    [InlineData(5, 600)]
    [InlineData(590, 600)]
    public void Boundary_Positions_Are_Kept(double position, double duration)
    {
        var store = new ResumeStore(new FakeStorage());

        store.Save("a.mp4", position, duration, Now);

        Assert.Equal(position, store.TryGet("a.mp4"));
    }

    [Fact]
    public void Finishing_A_File_Clears_Its_Entry()
    {
        var store = new ResumeStore(new FakeStorage());
        store.Save("a.mp4", 100, 600, Now);

        store.Save("a.mp4", 598, 600, Now.AddMinutes(1));

        Assert.Null(store.TryGet("a.mp4"));
    }

    [Fact]
    public void Saving_Again_Replaces_The_Old_Position()
    {
        var store = new ResumeStore(new FakeStorage());
        store.Save("a.mp4", 100, 600, Now);
        store.Save("a.mp4", 200, 600, Now.AddMinutes(1));

        Assert.Single(store.Entries);
        Assert.Equal(200, store.TryGet("a.mp4"));
    }

    [Fact]
    public void Oldest_Entries_Are_Pruned_Past_The_Cap()
    {
        var store = new ResumeStore(new FakeStorage());
        for (int i = 0; i < ResumeStore.MaxEntries + 5; i++)
        {
            store.Save($"f{i}.mp4", 60, 600, Now.AddSeconds(i));
        }

        Assert.Equal(ResumeStore.MaxEntries, store.Entries.Count);
        Assert.Null(store.TryGet("f0.mp4"));
        Assert.Null(store.TryGet("f4.mp4"));
        Assert.NotNull(store.TryGet("f5.mp4"));
        Assert.NotNull(store.TryGet($"f{ResumeStore.MaxEntries + 4}.mp4"));
    }

    [Theory]
    [InlineData("not json {{{")]
    [InlineData("[1,2]")]
    [InlineData("")]
    [InlineData("{\"entries\":[{\"location\":\"a.mp4\"},{\"x\":1},5]}")]
    public void Corrupt_Or_Malformed_Content_Gives_No_Entries(string content)
    {
        var store = new ResumeStore(new FakeStorage { Content = content });
        store.Load();

        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Failing_Storage_Keeps_Entries_In_Memory()
    {
        var store = new ResumeStore(new FakeStorage { Fail = true });
        store.Load();

        store.Save("a.mp4", 100, 600, Now);

        Assert.Equal(100, store.TryGet("a.mp4"));
    }

    [Fact]
    public void Remove_Forgets_The_File()
    {
        var store = new ResumeStore(new FakeStorage());
        store.Save("a.mp4", 100, 600, Now);

        store.Remove("A.mp4");

        Assert.Null(store.TryGet("a.mp4"));
    }

    [Fact]
    public void Clear_Forgets_Every_File()
    {
        var store = new ResumeStore(new FakeStorage());
        store.Save("a.mp4", 100, 600, Now);
        store.Save("b.mp4", 200, 600, Now);

        store.Clear();

        Assert.Empty(store.Entries);
        Assert.Null(store.TryGet("b.mp4"));
    }
}
