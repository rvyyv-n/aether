using Aether.Core.Playback.Resume;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class ResumeStoreExtendedTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private sealed class MemoryStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public string? Read() => Content;
        public void Write(string content) => Content = content;
    }

    [Theory]
    // Total duration must be strictly greater than SkipStart (5) + SkipEnd (10) = 15
    [InlineData(5.0, 15.0, false)] // duration == 15 is not strictly greater
    [InlineData(7.5, 15.0, false)]
    [InlineData(5.0, 15.01, true)] // duration > 15 and position in [5.0, 5.01]
    [InlineData(5.0, 14.99, false)]
    // Position boundaries relative to SkipStart = 5
    [InlineData(4.9999, 100.0, false)]
    [InlineData(5.0000, 100.0, true)]
    // Position boundaries relative to duration - SkipEnd = 90
    [InlineData(90.0000, 100.0, true)]
    [InlineData(90.0001, 100.0, false)]
    // Negative, non-finite or inverted values
    [InlineData(-1.0, 100.0, false)]
    [InlineData(50.0, -100.0, false)]
    [InlineData(double.NaN, 100.0, false)]
    [InlineData(50.0, double.NaN, false)]
    [InlineData(double.PositiveInfinity, 100.0, false)]
    [InlineData(50.0, double.PositiveInfinity, false)]
    public void IsWorthResuming_StrictBoundaryValidation(double position, double duration, bool expected)
    {
        Assert.Equal(expected, ResumeStore.IsWorthResuming(position, duration));
    }

    [Fact]
    public void SaveAndLookup_CaseInsensitiveMatching()
    {
        var storage = new MemoryStorage();
        var store = new ResumeStore(storage);

        store.Save(@"C:\Media\Videos\Ep01.MKV", 150.0, 1200.0, BaseTime);

        // Lookup with different casing
        Assert.Equal(150.0, store.TryGet(@"c:\media\videos\ep01.mkv"));
        Assert.Equal(150.0, store.TryGet(@"C:\MEDIA\VIDEOS\EP01.MKV"));

        // Remove with different casing
        store.Remove(@"c:\media\VIDEOS\ep01.MKV");
        Assert.Null(store.TryGet(@"C:\Media\Videos\Ep01.MKV"));
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Save_PositionNearStartOrEnd_EvictsExistingEntry()
    {
        var storage = new MemoryStorage();
        var store = new ResumeStore(storage);

        // Initial save in the middle of playback
        store.Save("video.mp4", 300.0, 600.0, BaseTime);
        Assert.Equal(300.0, store.TryGet("video.mp4"));

        // Later update lands near end (e.g. credits rolling, 595s out of 600s)
        store.Save("video.mp4", 595.0, 600.0, BaseTime.AddMinutes(5));

        // Since 595 is not worth resuming, the existing entry is removed
        Assert.Null(store.TryGet("video.mp4"));
        Assert.Empty(store.Entries);
    }

    [Fact]
    public void Save_PrunesStrictlyAtMaxEntriesCap_PreservingNewestTimestamps()
    {
        var storage = new MemoryStorage();
        var store = new ResumeStore(storage);

        // Populate exactly MaxEntries entries
        for (int i = 0; i < ResumeStore.MaxEntries; i++)
        {
            store.Save($"file_{i}.mp4", 50.0, 500.0, BaseTime.AddMinutes(i));
        }
        Assert.Equal(ResumeStore.MaxEntries, store.Entries.Count);

        // Add 201st entry with a newer timestamp
        store.Save("file_newest.mp4", 50.0, 500.0, BaseTime.AddMinutes(ResumeStore.MaxEntries + 10));

        // Entry count stays capped at 200
        Assert.Equal(ResumeStore.MaxEntries, store.Entries.Count);
        // Oldest entry (file_0) must have been pruned
        Assert.Null(store.TryGet("file_0.mp4"));
        // Newest entry must exist
        Assert.Equal(50.0, store.TryGet("file_newest.mp4"));

        // Add an entry with timestamp older than existing entries: it should be pruned immediately
        store.Save("file_ancient.mp4", 50.0, 500.0, BaseTime.AddMinutes(-500));
        Assert.Equal(ResumeStore.MaxEntries, store.Entries.Count);
        Assert.Null(store.TryGet("file_ancient.mp4"));
    }

    [Fact]
    public void SerializeAndParse_RoundTripsAccurately()
    {
        ResumeEntry[] original =
        [
            new ResumeEntry(@"C:\v\one.mp4", 25.5, 300.0, BaseTime),
            new ResumeEntry(@"C:\v\two.mkv", 140.2, 800.0, BaseTime.AddHours(1))
        ];

        string json = ResumeStore.Serialize(original);
        ResumeEntry[] parsed = ResumeStore.Parse(json);

        Assert.Equal(2, parsed.Length);
        Assert.Equal(original[0].Location, parsed[0].Location);
        Assert.Equal(original[0].Position, parsed[0].Position);
        Assert.Equal(original[0].Duration, parsed[0].Duration);
        Assert.Equal(original[0].SavedAt, parsed[0].SavedAt);

        Assert.Equal(original[1].Location, parsed[1].Location);
        Assert.Equal(original[1].Position, parsed[1].Position);
    }

    [Fact]
    public void Parse_DeduplicatesLocationsInJson()
    {
        // JSON containing duplicated location retains only the first occurrence
        string json = """
        {
            "version": 1,
            "entries": [
                { "location": "c:\\v.mp4", "position": 20, "duration": 100, "saved": "2026-10-11T12:00:00+00:00" },
                { "location": "C:\\V.MP4", "position": 40, "duration": 100, "saved": "2026-10-11T13:00:00+00:00" }
            ]
        }
        """;

        ResumeEntry[] parsed = ResumeStore.Parse(json);
        Assert.Single(parsed);
        Assert.Equal(20, parsed[0].Position);
    }

    [Fact]
    public void Parse_FiltersOutEntriesNotWorthResuming()
    {
        string json = """
        {
            "version": 1,
            "entries": [
                { "location": "valid.mp4", "position": 20, "duration": 100, "saved": "2026-10-11T12:00:00+00:00" },
                { "location": "at_start.mp4", "position": 2, "duration": 100, "saved": "2026-10-11T12:00:00+00:00" },
                { "location": "at_end.mp4", "position": 98, "duration": 100, "saved": "2026-10-11T12:00:00+00:00" }
            ]
        }
        """;

        ResumeEntry[] parsed = ResumeStore.Parse(json);
        Assert.Single(parsed);
        Assert.Equal("valid.mp4", parsed[0].Location);
    }

    [Fact]
    public void DefaultPath_EndsWithExpectedDirectoryAndFileName()
    {
        string defaultPath = ResumeStore.DefaultPath();
        Assert.EndsWith(Path.Combine("Aether", "resume.json"), defaultPath);
    }
}
