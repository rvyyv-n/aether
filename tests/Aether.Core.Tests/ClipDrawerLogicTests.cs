using System.Globalization;
using Aether.Core.Playback;
using Aether.Core.Playback.Clips;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class ClipDrawerLogicTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 18, 0, 0, TimeSpan.FromHours(1));
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public bool Fail { get; set; }
        public string? Read() => Fail ? throw new IOException() : Content;
        public void Write(string content) => Content = Fail ? throw new IOException() : content;
    }

    private static SavedClip Clip(string path, long size = 1000, double length = 40, DateTimeOffset? at = null) =>
        new(path, size, length, at ?? Now);

    private static ClipShelf NewShelf(FakeStorage storage)
    {
        var shelf = new ClipShelf(storage);
        shelf.Load();
        return shelf;
    }

    [Fact]
    public void Shelf_ListsNewestFirst_AndMovesAReExportToTheTop()
    {
        ClipShelf shelf = NewShelf(new FakeStorage());
        shelf.Add(Clip(@"C:\c\a.mp4"));
        shelf.Add(Clip(@"C:\c\b.mp4"));
        shelf.Add(Clip(@"C:\C\A.MP4", size: 5));

        Assert.Equal([@"C:\C\A.MP4", @"C:\c\b.mp4"], shelf.Items.Select(i => i.Path));
    }

    [Fact]
    public void Shelf_KeepsAtMostMaxItems()
    {
        ClipShelf shelf = NewShelf(new FakeStorage());
        for (int i = 0; i < ClipShelf.MaxItems + 5; i++)
        {
            shelf.Add(Clip($@"C:\c\{i}.mp4"));
        }

        Assert.Equal(ClipShelf.MaxItems, shelf.Items.Count);
        Assert.Equal($@"C:\c\{ClipShelf.MaxItems + 4}.mp4", shelf.Items[0].Path);
    }

    [Fact]
    public void Shelf_DismissRemovesOnlyThatRow()
    {
        ClipShelf shelf = NewShelf(new FakeStorage());
        shelf.Add(Clip(@"C:\c\a.mp4"));
        shelf.Add(Clip(@"C:\c\b.mp4"));

        shelf.Remove(@"c:\C\A.mp4");

        Assert.Equal([@"C:\c\b.mp4"], shelf.Items.Select(i => i.Path));
    }

    [Fact]
    public void Shelf_RoundTripsThroughStorage()
    {
        var storage = new FakeStorage();
        ClipShelf shelf = NewShelf(storage);
        SavedClip clip = Clip(@"C:\c\a.mp4", size: 4_404_019, length: 40.5);
        shelf.Add(clip);

        Assert.Equal([clip], NewShelf(storage).Items);
    }

    [Fact]
    public void Shelf_ChangedFires_AndAFailedWriteKeepsTheList()
    {
        var storage = new FakeStorage();
        ClipShelf shelf = NewShelf(storage);
        int changes = 0;
        shelf.Changed += (_, _) => changes++;
        storage.Fail = true;

        shelf.Add(Clip(@"C:\c\a.mp4"));

        Assert.Equal(1, changes);
        Assert.Single(shelf.Items);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("""{"items":5}""")]
    public void Shelf_BadFileLoadsEmpty(string? content)
    {
        Assert.Empty(NewShelf(new FakeStorage { Content = content }).Items);
    }

    [Fact]
    public void Shelf_SkipsMalformedEntries()
    {
        const string json = """
            {"version":1,"items":[
              {"path":"C:\\c\\a.mp4","size":10,"length":5,"saved":"2026-10-11T18:00:00+01:00"},
              {"path":"","saved":"2026-10-11T18:00:00+01:00"},
              {"path":"C:\\c\\b.mp4"},
              {"path":"C:\\c\\c.mp4","size":"big","length":-3,"saved":"2026-10-11T18:00:00+01:00"}
            ]}
            """;

        SavedClip[] items = [.. NewShelf(new FakeStorage { Content = json }).Items];

        Assert.Equal([@"C:\c\a.mp4", @"C:\c\c.mp4"], items.Select(i => i.Path));
        Assert.Equal(0, items[1].SizeBytes);
        Assert.Equal(0, items[1].Length);
    }

    [Fact]
    public void Chip_FollowsRangeClipsAndFfmpeg()
    {
        ClipRange complete = default(ClipRange).SetIn(30).SetOut(70);
        ClipRange halfSet = default(ClipRange).SetIn(30);

        Assert.Equal(ClipChipMode.Hidden, ClipDisplay.Chip(default, 0, true));
        Assert.Equal(ClipChipMode.Hidden, ClipDisplay.Chip(halfSet, 0, true));
        Assert.Equal(ClipChipMode.Clips, ClipDisplay.Chip(halfSet, 2, true));
        Assert.Equal(ClipChipMode.Export, ClipDisplay.Chip(complete, 0, true));
        Assert.Equal(ClipChipMode.Export, ClipDisplay.Chip(complete, 4, true));
        Assert.Equal(ClipChipMode.ExportUnavailable, ClipDisplay.Chip(complete, 4, false));
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(348_160, "340 KB")]
    [InlineData(1_048_535, "1 MB")]
    [InlineData(4_404_019, "4.2 MB")]
    [InlineData(1_181_116_006, "1.1 GB")]
    public void FormatSize_ReadsLikeExplorer(long bytes, string expected)
    {
        Assert.Equal(expected, ClipDisplay.FormatSize(bytes, Invariant));
    }

    [Fact]
    public void FormatSaved_TimeTodayThenYesterdayThenDate()
    {
        Assert.Equal("17:42", ClipDisplay.FormatSaved(Now.AddMinutes(-18), Now, Invariant, "Yesterday"));
        Assert.Equal("Yesterday", ClipDisplay.FormatSaved(Now.AddDays(-1), Now, Invariant, "Yesterday"));
        Assert.Equal("October 08", ClipDisplay.FormatSaved(Now.AddDays(-3), Now, Invariant, "Yesterday"));
    }

    [Fact]
    public void Details_JoinsSizeLengthAndTime()
    {
        SavedClip clip = Clip(@"C:\c\a.mp4", size: 4_404_019, length: 40.9, at: Now.AddMinutes(-18));

        Assert.Equal("4.2 MB · 00:40 · 17:42", ClipDisplay.Details(clip, Now, Invariant, "Yesterday"));
    }

    [Fact]
    public void IsMissing_AsksTheDisk()
    {
        SavedClip clip = Clip(@"C:\c\a.mp4");

        Assert.True(ClipDisplay.IsMissing(clip, _ => false));
        Assert.False(ClipDisplay.IsMissing(clip, _ => true));
    }

    [Theory]
    [InlineData(@"D:\Captures\match-01.mp4", "match-01")]
    [InlineData(@"D:\Captures\  spaced .mkv", "spaced")]
    [InlineData(@"D:\Captures\.mp4", "Clip")]
    [InlineData("a\"b|c.mp4", "a_b_c")]
    public void NamePrefix_IsTheSafeSourceName(string source, string expected)
    {
        Assert.Equal(expected, ClipDisplay.NamePrefix(source));
    }

    [Fact]
    public void NamePrefix_StaysShort()
    {
        Assert.Equal(60, ClipDisplay.NamePrefix(@"D:\" + new string('x', 200) + ".mp4").Length);
    }

    [Fact]
    public void Name_IsTheFileName()
    {
        Assert.Equal("match-01_20261011_180203.mp4", Clip(@"C:\c\match-01_20261011_180203.mp4").Name);
    }

    [Fact]
    public void Shelf_KeepsTheEncoderOfAPreciseClip_AndOlderEntriesHaveNone()
    {
        var storage = new FakeStorage();
        ClipShelf shelf = NewShelf(storage);
        shelf.Add(new SavedClip(@"C:\c.mp4", 1000, 40, Now, "Nvenc"));
        shelf.Add(Clip(@"C:\c.mp4"));

        ClipShelf reloaded = NewShelf(storage);

        Assert.Null(reloaded.Items[0].Encoder);
        Assert.Equal("Nvenc", reloaded.Items[1].Encoder);
        Assert.DoesNotContain("encoder", ClipShelf.Serialize([Clip(@"C:\c.mp4")]));
    }

    [Fact]
    public void Details_AddsTheEncoderOnlyWhenGiven()
    {
        SavedClip clip = Clip(@"C:\c.mp4", size: 4_404_019, length: 40);
        string plain = ClipDisplay.Details(clip, Now, Invariant, "Yesterday");

        Assert.Equal(plain + " · NVIDIA", ClipDisplay.Details(clip, Now, Invariant, "Yesterday", "NVIDIA"));
    }
    [Theory]
    [InlineData(false, false, true, true, ClipHint.Fast)]
    [InlineData(false, true, true, true, ClipHint.Fast)]
    [InlineData(true, true, true, true, ClipHint.TooSmall)]
    [InlineData(true, true, false, false, ClipHint.TooSmall)]
    [InlineData(true, false, true, true, ClipHint.LookEmpty)]
    [InlineData(true, false, true, false, ClipHint.Precise)]
    [InlineData(true, false, false, true, ClipHint.Precise)]
    [InlineData(true, false, false, false, ClipHint.Precise)]
    public void Hint_FirstMatchWins_FastThenTooSmallThenEmptyLookThenPrecise(
        bool precise, bool tooSmall, bool includeLook, bool lookIsEmpty, ClipHint expected) =>
        Assert.Equal(expected, ClipDisplay.Hint(precise, tooSmall, includeLook, lookIsEmpty));
}
