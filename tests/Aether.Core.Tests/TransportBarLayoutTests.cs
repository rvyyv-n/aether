using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class TransportBarLayoutTests
{
    [Fact]
    public void DefaultLayout_MatchesStockPlayerBarOrderAndVisibility()
    {
        IReadOnlyList<TransportBarItem> items = TransportBarLayout.Parse(null);

        Assert.Equal(8, items.Count);
        Assert.Equal(
        [
            new(TransportBarControl.Speed, true),
            new(TransportBarControl.Quality, true),
            new(TransportBarControl.Skip, true),
            new(TransportBarControl.FrameStep, false),
            new(TransportBarControl.Tracks, false),
            new(TransportBarControl.Loop, false),
            new(TransportBarControl.AlwaysOnTop, false),
            new(TransportBarControl.Chapters, false),
        ], items);
    }

    [Fact]
    public void ParseAndFormat_DeduplicatesIgnoresUnknownAndAppendsMissingDefaults()
    {
        IReadOnlyList<TransportBarItem> parsed = TransportBarLayout.Parse("tracks:1,bogus:1,tracks:0,loop:1");

        Assert.Equal(8, parsed.Count);
        Assert.Equal(TransportBarControl.Tracks, parsed[0].Control);
        Assert.True(parsed[0].Visible);
        Assert.Equal(TransportBarControl.Loop, parsed[1].Control);
        Assert.True(parsed[1].Visible);
        Assert.Contains(parsed, item => item.Control == TransportBarControl.Speed && item.Visible);

        string roundTrip = TransportBarLayout.Format(parsed);
        Assert.Equal(parsed, TransportBarLayout.Parse(roundTrip));
    }

    [Fact]
    public void SetVisibleAndMove_UpdateVisibilityOrderAndClampBoundaries()
    {
        string value = TransportBarLayout.SetVisible(TransportBarLayout.DefaultValue, TransportBarControl.FrameStep, true);
        Assert.Contains(TransportBarControl.FrameStep, TransportBarLayout.VisibleControls(value));

        value = TransportBarLayout.Move(value, TransportBarControl.FrameStep, -1);
        IReadOnlyList<TransportBarItem> movedUp = TransportBarLayout.Parse(value);
        Assert.Equal(TransportBarControl.FrameStep, movedUp[2].Control);

        string atTop = TransportBarLayout.Move(value, TransportBarControl.Speed, -1);
        Assert.Equal(TransportBarControl.Speed, TransportBarLayout.Parse(atTop)[0].Control);

        string atBottom = TransportBarLayout.Move(value, TransportBarControl.Chapters, +1);
        Assert.Equal(TransportBarControl.Chapters, TransportBarLayout.Parse(atBottom)[^1].Control);
    }

    [Fact]
    public void SettingsStore_RoundTripsTransportBarControls()
    {
        var storage = new MemorySettingsStorage();
        var store = new SettingsStore(storage);

        string custom = TransportBarLayout.Move(
            TransportBarLayout.SetVisible(store.Current.TransportBarControls, TransportBarControl.Loop, true),
            TransportBarControl.Loop,
            -2);
        Assert.True(store.Set(SettingKeys.TransportBarControls, custom));
        Assert.False(store.Set(SettingKeys.TransportBarControls, "bad\nline"));
        store.Save();

        var reloaded = new SettingsStore(storage);
        reloaded.Load();
        Assert.Equal(custom, reloaded.Current.TransportBarControls);
    }

    private sealed class MemorySettingsStorage : ISettingsStorage
    {
        private string? _text;
        public string? Read() => _text;
        public void Write(string contents) => _text = contents;
    }
}
