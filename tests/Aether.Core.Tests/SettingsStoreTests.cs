using System.Linq;
using Aether.Core.Playback.Settings;
using Xunit;

namespace Aether.Core.Tests;

public class SettingsStoreTests
{
    private sealed class FakeStorage : ISettingsStorage
    {
        public string? Content { get; set; }
        public int Writes { get; private set; }
        public string? Read() => Content;
        public void Write(string content) { Content = content; Writes++; }
    }

    private static SettingsStore NewStore(string? content = null)
    {
        var store = new SettingsStore(new FakeStorage { Content = content });
        store.Load();
        return store;
    }

    [Fact]
    public void AlwaysOnTop_Survives_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        first.Set(SettingKeys.AlwaysOnTop, true);
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();

        Assert.True(second.Current.AlwaysOnTop);
    }

    [Fact]
    public void Missing_File_Gives_Defaults()
    {
        var store = NewStore();

        Assert.Equal(new AetherSettings(), store.Current);
        Assert.Equal("auto-safe", store.Current.HardwareDecoding);
        Assert.False(store.PendingRestart);
    }

    [Theory]
    [InlineData("not json {{{")]
    [InlineData("[1,2,3]")]
    [InlineData("")]
    public void Corrupt_File_Falls_Back_To_Defaults(string content)
    {
        Assert.Equal(new AetherSettings(), NewStore(content).Current);
    }

    [Fact]
    public void Invalid_Value_Falls_Back_For_That_Setting_Only()
    {
        var store = NewStore("""
            { "video.hwdec": "vulkan", "video.brightness": 500, "audio.exclusive": "yes",
              "video.contrast": 12, "unknown.key": 1, "playback.loop": true }
            """);

        Assert.Equal("auto-safe", store.Current.HardwareDecoding);
        Assert.Equal(0, store.Current.Brightness);
        Assert.False(store.Current.ExclusiveMode);
        Assert.Equal(12, store.Current.Contrast);
        Assert.True(store.Current.LoopByDefault);
    }

    [Fact]
    public void Set_Rejects_Unknown_Key_Wrong_Type_And_Disallowed_Value()
    {
        var store = NewStore();

        Assert.False(store.Set("nope", true));
        Assert.False(store.Set(SettingKeys.PreciseSeeking, "yes"));
        Assert.False(store.Set(SettingKeys.HardwareDecoding, "vulkan"));
        Assert.Equal(new AetherSettings(), store.Current);
    }

    [Fact]
    public void Reset_Restores_One_Setting_And_ResetAll_Restores_Everything()
    {
        var store = NewStore();
        store.Set(SettingKeys.Upscaling, "lanczos");
        store.Set(SettingKeys.Contrast, 6);

        Assert.False(store.IsDefault(SettingKeys.Upscaling));
        Assert.True(store.Reset(SettingKeys.Upscaling));
        Assert.True(store.IsDefault(SettingKeys.Upscaling));
        Assert.Equal(6, store.Current.Contrast);

        store.ResetAll();
        Assert.Equal(new AetherSettings(), store.Current);
    }

    [Fact]
    public void Changed_Fires_Only_When_The_Value_Differs()
    {
        var store = NewStore();
        var keys = new System.Collections.Generic.List<string>();
        store.Changed += (_, e) => keys.Add(e.Key);

        store.Set(SettingKeys.AlwaysOnTop, true);
        store.Set(SettingKeys.AlwaysOnTop, true);
        store.Reset(SettingKeys.AlwaysOnTop);

        Assert.Equal([SettingKeys.AlwaysOnTop, SettingKeys.AlwaysOnTop], keys);
    }

    [Fact]
    public void Restart_Flags_Match_The_Design()
    {
        var store = NewStore();
        var flagged = SettingDefinitions.Keys.Where(store.RequiresRestart).ToArray();

        Assert.Equal([SettingKeys.HardwareDecoding, SettingKeys.Passthrough, SettingKeys.CustomMpvOptions], flagged);
    }

    [Fact]
    public void PendingRestart_Tracks_Difference_From_The_Loaded_Value()
    {
        var store = NewStore();

        store.Set(SettingKeys.PreciseSeeking, true);
        Assert.False(store.PendingRestart);

        store.Set(SettingKeys.HardwareDecoding, "no");
        Assert.True(store.PendingRestart);
        Assert.True(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.False(store.IsRestartPending(SettingKeys.Passthrough));

        store.Set(SettingKeys.HardwareDecoding, "auto-safe");
        Assert.False(store.PendingRestart);
    }

    [Fact]
    public void Save_Then_Load_Round_Trips_Through_Storage()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        first.Set(SettingKeys.HardwareDecoding, "d3d11va-copy");
        first.Set(SettingKeys.CustomMpvOptions, "deband-iterations=2\nsub-auto=fuzzy");
        first.Set(SettingKeys.Brightness, -7);
        first.Set(SettingKeys.StreamingQuality, "best");
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();

        Assert.Equal(first.Current, second.Current);
        Assert.False(second.PendingRestart);
    }

    [Fact]
    public void File_Storage_Writes_Atomically_And_Replaces_The_Previous_File()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "aether-settings-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new FileSettingsStorage(System.IO.Path.Combine(dir, "sub", "settings.json"));
            Assert.Null(storage.Read());

            storage.Write("one");
            storage.Write("two");

            Assert.Equal("two", storage.Read());
            Assert.Equal([storage.Path], System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(storage.Path)!));
        }
        finally
        {
            if (System.IO.Directory.Exists(dir))
            {
                System.IO.Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void Options_Validator_Reports_Bad_Lines_With_Numbers()
    {
        var errors = MpvOptionsValidator.Validate("deband-iterations=2\nvo foo\n\n# comment\n=3\nsub auto=1\nlast=ok\r\nfuzzy");

        Assert.Equal([2, 5, 6, 8], errors.Select(e => e.Line));
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e.Message)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n\n# only a comment\n")]
    [InlineData("hwdec-codecs=all\nvf=\nscale-antiring=0.5")]
    public void Options_Validator_Accepts_Well_Formed_Text(string text)
    {
        Assert.Empty(MpvOptionsValidator.Validate(text));
    }

    [Fact]
    public void Every_Definition_Default_Is_In_Its_Allowed_Values()
    {
        var defaults = SettingDefinitions.Defaults;
        foreach (var definition in SettingDefinitions.All)
        {
            var defaultValue = definition.GetValue(defaults);
            Assert.NotNull(defaultValue);
            var applied = definition.TryWith(defaults, defaultValue);
            Assert.NotNull(applied);
            Assert.Equal(defaultValue, definition.GetValue(applied));
        }
    }

    [Fact]
    public void Every_Restart_Flagged_Key_Reports_RequiresRestart()
    {
        var store = NewStore();
        foreach (var definition in SettingDefinitions.All)
        {
            Assert.Equal(definition.RequiresRestart, store.RequiresRestart(definition.Key));
        }
        Assert.False(store.RequiresRestart("nonexistent.setting.key"));
    }

    [Fact]
    public void Reset_Of_One_Key_Leaves_Others_Intact()
    {
        var store = NewStore();
        store.Set(SettingKeys.QualityPreset, "high");
        store.Set(SettingKeys.Brightness, 25);
        store.Set(SettingKeys.LoopByDefault, true);

        Assert.True(store.Reset(SettingKeys.Brightness));

        Assert.True(store.IsDefault(SettingKeys.Brightness));
        Assert.Equal(0, store.Current.Brightness);
        Assert.Equal("high", store.Current.QualityPreset);
        Assert.True(store.Current.LoopByDefault);
    }

    [Fact]
    public void Changed_Event_Fires_Once_Per_Successful_Set()
    {
        var store = NewStore();
        var changes = new System.Collections.Generic.List<string>();
        store.Changed += (_, e) => changes.Add(e.Key);

        // Setting current value should not fire Changed.
        store.Set(SettingKeys.HardwareDecoding, "auto-safe");
        Assert.Empty(changes);

        // Changing the value fires once.
        store.Set(SettingKeys.HardwareDecoding, "d3d11va");
        Assert.Single(changes);
        Assert.Equal(SettingKeys.HardwareDecoding, changes[0]);

        // Setting the identical value again should not fire.
        store.Set(SettingKeys.HardwareDecoding, "d3d11va");
        Assert.Single(changes);

        // Setting another key fires once.
        store.Set(SettingKeys.Contrast, 10);
        Assert.Equal(2, changes.Count);
        Assert.Equal(SettingKeys.Contrast, changes[1]);
    }

    [Fact]
    public void ResetAll_Clears_PendingRestart()
    {
        var store = NewStore();
        Assert.False(store.PendingRestart);

        store.Set(SettingKeys.HardwareDecoding, "no");
        Assert.True(store.PendingRestart);
        Assert.True(store.IsRestartPending(SettingKeys.HardwareDecoding));

        store.ResetAll();

        Assert.False(store.PendingRestart);
        Assert.False(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.Equal(new AetherSettings(), store.Current);
    }
}

