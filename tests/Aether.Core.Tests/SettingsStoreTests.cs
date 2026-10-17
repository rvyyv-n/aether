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
    public void Playback_Option_Settings_Default_As_Designed()
    {
        var s = new AetherSettings();

        Assert.True(s.PreventSleep);
        Assert.True(s.PitchCorrection);
        Assert.False(s.StereoDownmix);
        Assert.False(s.Deinterlace);
        Assert.Equal("auto", s.DeinterlaceMode);
        Assert.Equal("auto", s.AspectRatio);
        Assert.Equal("", s.ScreenshotDirectory);
        Assert.Equal("png", s.ScreenshotFormat);
        Assert.Equal("Aether-%F-%P", s.ScreenshotTemplate);
        Assert.Equal(150, s.NetworkCacheMegabytes);
        Assert.Equal("", s.HttpProxy);
        Assert.Equal("tcp", s.RtspTransport);
        Assert.False(s.RepairAviIndex);
        Assert.Equal("default", s.SkipDeblocking);
    }

    [Fact]
    public void Playback_Option_Settings_Reset_To_Their_Defaults_And_Survive_Save_And_Load()
    {
        var storage = new FakeStorage();
        var first = new SettingsStore(storage);
        first.Set(SettingKeys.PreventSleep, false);
        first.Set(SettingKeys.PitchCorrection, false);
        first.Set(SettingKeys.StereoDownmix, true);
        first.Set(SettingKeys.Deinterlace, true);
        first.Set(SettingKeys.DeinterlaceMode, "bwdif");
        first.Set(SettingKeys.AspectRatio, "21:9");
        first.Set(SettingKeys.ScreenshotDirectory, @"D:\Shots");
        first.Set(SettingKeys.ScreenshotFormat, "jpg");
        first.Set(SettingKeys.ScreenshotTemplate, "shot-%n");
        first.Set(SettingKeys.NetworkCacheMegabytes, 400);
        first.Set(SettingKeys.HttpProxy, "http://proxy:3128");
        first.Set(SettingKeys.RtspTransport, "udp");
        first.Set(SettingKeys.RepairAviIndex, true);
        first.Set(SettingKeys.SkipDeblocking, "all");
        first.Save();

        var second = new SettingsStore(storage);
        second.Load();
        Assert.Equal(first.Current, second.Current);

        second.ResetAll();
        Assert.Equal(new AetherSettings(), second.Current);
    }

    [Theory]
    [InlineData(SettingKeys.DeinterlaceMode, "linear")]
    [InlineData(SettingKeys.AspectRatio, "5:4")]
    [InlineData(SettingKeys.ScreenshotFormat, "gif")]
    [InlineData(SettingKeys.RtspTransport, "smoke")]
    [InlineData(SettingKeys.SkipDeblocking, "none")]
    public void Playback_Option_Choices_Reject_Unknown_Values(string key, string value)
    {
        var store = NewStore();

        Assert.False(store.Set(key, value));
        Assert.True(store.IsDefault(key));
    }

    [Theory]
    [InlineData(15)]
    [InlineData(2049)]
    public void Network_Cache_Rejects_Out_Of_Range_Sizes(int megabytes)
    {
        var store = NewStore();

        Assert.False(store.Set(SettingKeys.NetworkCacheMegabytes, megabytes));
        Assert.Equal(150, store.Current.NetworkCacheMegabytes);
    }

    [Fact]
    public void Network_Cache_Change_Waits_For_A_Restart_But_Sleep_Prevention_Does_Not()
    {
        var store = NewStore();

        store.Set(SettingKeys.NetworkCacheMegabytes, 300);
        store.Set(SettingKeys.PreventSleep, false);

        Assert.True(store.IsRestartPending(SettingKeys.NetworkCacheMegabytes));
        Assert.False(store.IsRestartPending(SettingKeys.PreventSleep));
        Assert.False(store.RequiresRestart(SettingKeys.HttpProxy));
    }

    [Fact]
    public void Restart_Flags_Match_The_Design()
    {
        var store = NewStore();
        var flagged = SettingDefinitions.Keys.Where(store.RequiresRestart).ToArray();

        Assert.Equal(
            [
                SettingKeys.HardwareDecoding,
                SettingKeys.Passthrough,
                SettingKeys.NetworkCacheMegabytes,
                SettingKeys.RepairAviIndex,
                SettingKeys.SkipDeblocking,
                SettingKeys.CustomMpvOptions,
            ],
            flagged);
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
    public void MarkStarted_Clears_PendingRestart_For_The_Started_Values_Only()
    {
        var store = NewStore();
        int started = 0;
        store.Started += (_, _) => started++;

        store.Set(SettingKeys.HardwareDecoding, "no");
        AetherSettings snapshot = store.Current;
        store.Set(SettingKeys.Passthrough, true); // changed after the engine took its snapshot
        store.MarkStarted(snapshot);

        Assert.Equal(1, started);
        Assert.False(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.True(store.IsRestartPending(SettingKeys.Passthrough));

        store.Set(SettingKeys.HardwareDecoding, "auto-safe");
        Assert.True(store.IsRestartPending(SettingKeys.HardwareDecoding)); // the engine runs with "no" now
    }

    private sealed class OverlapStorage : ISettingsStorage
    {
        private int _writing;
        public int Overlaps { get; private set; }
        public string? Read() => null;
        public void Write(string content)
        {
            if (Interlocked.Increment(ref _writing) > 1)
            {
                Overlaps++;
            }
            Thread.Sleep(1);
            Interlocked.Decrement(ref _writing);
        }
    }

    [Fact]
    public void Overlapping_Saves_Write_One_At_A_Time()
    {
        var storage = new OverlapStorage();
        var store = new SettingsStore(storage);

        Parallel.For(0, 32, i =>
        {
            store.Set(SettingKeys.Brightness, i);
            store.Save();
        });

        Assert.Equal(0, storage.Overlaps);
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
        Assert.Equal(MpvOptionErrorKind.MissingEquals, errors[0].Kind);
        Assert.Equal(MpvOptionErrorKind.MissingName, errors[1].Kind);
        Assert.Equal(MpvOptionErrorKind.InvalidName, errors[2].Kind);
        Assert.Equal("sub auto", errors[2].Key);
        Assert.Equal(MpvOptionErrorKind.MissingEquals, errors[3].Kind);
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
    [Fact]
    public void Every_Setting_Default_Value_Round_Trips_Through_Save_And_Load()
    {
        var storage = new FakeStorage();
        var store = new SettingsStore(storage);
        store.Save();

        var loadedStore = new SettingsStore(storage);
        loadedStore.Load();

        Assert.Equal(SettingDefinitions.Defaults, loadedStore.Current);
        foreach (var def in SettingDefinitions.All)
        {
            Assert.Equal(def.GetValue(SettingDefinitions.Defaults), def.GetValue(loadedStore.Current));
            Assert.True(loadedStore.IsDefault(def.Key));
        }
    }

    [Fact]
    public void Reset_And_ResetAll_Restore_Defaults_And_Raise_Changed()
    {
        var store = NewStore();
        var changedKeys = new System.Collections.Generic.List<string>();
        store.Changed += (_, e) => changedKeys.Add(e.Key);

        Assert.True(store.Reset(SettingKeys.QualityPreset));
        Assert.Empty(changedKeys);

        store.Set(SettingKeys.QualityPreset, "high");
        store.Set(SettingKeys.LoopByDefault, true);
        changedKeys.Clear();

        Assert.True(store.Reset(SettingKeys.QualityPreset));
        Assert.Equal("balanced", store.Current.QualityPreset);
        Assert.True(store.IsDefault(SettingKeys.QualityPreset));
        Assert.Single(changedKeys);
        Assert.Equal(SettingKeys.QualityPreset, changedKeys[0]);

        changedKeys.Clear();
        store.ResetAll();
        Assert.Equal(new AetherSettings(), store.Current);
        Assert.Contains(SettingKeys.LoopByDefault, changedKeys);
    }

    [Fact]
    public void Restart_Flags_For_HardwareDecoding_And_NonRestart_Setting()
    {
        var store = NewStore();

        Assert.True(store.RequiresRestart(SettingKeys.HardwareDecoding));
        Assert.False(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.False(store.PendingRestart);

        store.Set(SettingKeys.HardwareDecoding, "no");
        Assert.True(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.True(store.PendingRestart);

        store.Set(SettingKeys.HardwareDecoding, "auto-safe");
        Assert.False(store.IsRestartPending(SettingKeys.HardwareDecoding));
        Assert.False(store.PendingRestart);

        Assert.False(store.RequiresRestart(SettingKeys.LoopByDefault));
        Assert.False(store.IsRestartPending(SettingKeys.LoopByDefault));

        store.Set(SettingKeys.LoopByDefault, true);
        Assert.False(store.RequiresRestart(SettingKeys.LoopByDefault));
        Assert.False(store.IsRestartPending(SettingKeys.LoopByDefault));
        Assert.False(store.PendingRestart);

        store.Set(SettingKeys.LoopByDefault, false);
        Assert.False(store.IsRestartPending(SettingKeys.LoopByDefault));
        Assert.False(store.PendingRestart);
    }

    [Fact]
    public void Rejected_Values_Leave_Store_Unchanged()
    {
        var store = NewStore();
        AetherSettings before = store.Current;

        Assert.False(store.Set(SettingKeys.BatterySaver, "true"));
        Assert.False(store.Set(SettingKeys.BatterySaver, 123));

        Assert.False(store.Set(SettingKeys.Brightness, true));
        Assert.False(store.Set(SettingKeys.Brightness, "50"));

        Assert.False(store.Set(SettingKeys.QualityPreset, 42));

        Assert.False(store.Set(SettingKeys.QualityPreset, "ultra"));
        Assert.False(store.Set(SettingKeys.HardwareDecoding, "cuda"));
        Assert.False(store.Set(SettingKeys.Upscaling, "nearest"));
        Assert.False(store.Set(SettingKeys.Downscaling, "area"));
        Assert.False(store.Set(SettingKeys.HdrToneMapping, "unsupported"));
        Assert.False(store.Set(SettingKeys.SubtitleSize, "gigantic"));
        Assert.False(store.Set(SettingKeys.SubtitleOutline, "huge"));
        Assert.False(store.Set(SettingKeys.SubtitleOutlineColor, "pink"));
        Assert.False(store.Set(SettingKeys.SubtitleBackground, "striped"));
        Assert.False(store.Set(SettingKeys.SubtitleBold, "yes"));
        Assert.False(store.Set(SettingKeys.StreamingQuality, "8k"));

        Assert.False(store.Set(SettingKeys.Brightness, -101));
        Assert.False(store.Set(SettingKeys.Brightness, 101));
        Assert.False(store.Set(SettingKeys.Contrast, -150));
        Assert.False(store.Set(SettingKeys.Contrast, 200));
        Assert.False(store.Set(SettingKeys.Gamma, -1000));
        Assert.False(store.Set(SettingKeys.Gamma, 1000));
        Assert.False(store.Set(SettingKeys.Saturation, -101));
        Assert.False(store.Set(SettingKeys.Saturation, 101));

        Assert.Equal(before, store.Current);
    }

    [Fact]
    public void Partial_Settings_File_Loads_Defaults_For_Bad_Keys_Only()
    {
        string json = """
        {
            "playback.loop": true,
            "video.brightness": 45,
            "video.hwdec": "invalid-choice",
            "video.contrast": 999,
            "audio.exclusive": "not-a-bool",
            "streaming.quality": "best"
        }
        """;
        var store = NewStore(json);

        Assert.True(store.Current.LoopByDefault);
        Assert.Equal(45, store.Current.Brightness);
        Assert.Equal("best", store.Current.StreamingQuality);

        Assert.Equal("auto-safe", store.Current.HardwareDecoding);
        Assert.Equal(0, store.Current.Contrast);
        Assert.False(store.Current.ExclusiveMode);

        Assert.True(store.Current.BatterySaver);
        Assert.True(store.Current.ResumePlayback);
    }

    [Fact]
    public void Corrupt_Json_File_Loads_All_Defaults()
    {
        string brokenJson = "{ \"video.brightness\": 45, invalid json syntax here... ";
        var store = NewStore(brokenJson);
        Assert.Equal(SettingDefinitions.Defaults, store.Current);
    }

    [Fact]
    public void MpvOptionsValidator_Handles_All_Error_Kinds()
    {
        var errEquals = MpvOptionsValidator.Validate("vo-gpu\nno_equal_here");
        Assert.Equal(2, errEquals.Count);
        Assert.All(errEquals, e => Assert.Equal(MpvOptionErrorKind.MissingEquals, e.Kind));

        var errName = MpvOptionsValidator.Validate("=value\n--=value\n   =value");
        Assert.Equal(3, errName.Count);
        Assert.All(errName, e => Assert.Equal(MpvOptionErrorKind.MissingName, e.Kind));

        var errInvalid = MpvOptionsValidator.Validate("foo bar=1\nkey$name=2\nbad*key=3");
        Assert.Equal(3, errInvalid.Count);
        Assert.All(errInvalid, e => Assert.Equal(MpvOptionErrorKind.InvalidName, e.Kind));
        Assert.Equal("foo bar", errInvalid[0].Key);
        Assert.Equal("key$name", errInvalid[1].Key);
        Assert.Equal("bad*key", errInvalid[2].Key);

        string[] reservedExamples =
        [
            "vo=gpu", "wid=123", "gpu-context=d3d11", "gpu-api=d3d11", "hwdec=auto",
            "config=yes", "config-dir=/path", "include=/path", "input-ipc-server=pipe",
            "script=test.lua", "scripts=dir", "load-scripts=yes",
            "--VO=gpu-next", "--HWDEC=no", "--load-SCRIPTS=no"
        ];
        var errReserved = MpvOptionsValidator.Validate(string.Join("\n", reservedExamples));
        Assert.Equal(reservedExamples.Length, errReserved.Count);
        Assert.All(errReserved, e => Assert.Equal(MpvOptionErrorKind.Reserved, e.Kind));

        var errDup = MpvOptionsValidator.Validate("sub-font=Arial\nsub-font-size=40\nsub-font=Roboto\n--SUB-FONT=Consolas");
        Assert.Equal(2, errDup.Count);
        Assert.Equal(3, errDup[0].Line);
        Assert.Equal(MpvOptionErrorKind.Duplicate, errDup[0].Kind);
        Assert.Equal("sub-font", errDup[0].Key);
        Assert.Equal(4, errDup[1].Line);
        Assert.Equal(MpvOptionErrorKind.Duplicate, errDup[1].Kind);
        Assert.Equal("SUB-FONT", errDup[1].Key);
    }

    [Fact]
    public void MpvOptionsValidator_Handles_Comments_Blank_Lines_CrLf_And_Prefixed_Keys()
    {
        string text = "# Header comment\r\n\r\n--cache=yes\r\n   \r\n# Another comment\r\ndemuxer-max-bytes=209715200\r\n--sub-scale=1.5\r\n";
        var errors = MpvOptionsValidator.Validate(text);
        Assert.Empty(errors);
    }

    [Fact]
    public void HardwareDecodingOptions_Matches_SettingDefinitions()
    {
        var def = SettingDefinitions.All.First(d => d.Key == SettingKeys.HardwareDecoding);
        string[] expectedValues = ["auto-safe", "no", "d3d11va", "d3d11va-copy"];

        Assert.Equal(expectedValues, HardwareDecodingOptions.All.Select(o => o.Value));

        var defaults = SettingDefinitions.Defaults;
        foreach (var option in HardwareDecodingOptions.All)
        {
            Assert.NotNull(def.TryWith(defaults, option.Value));
            Assert.StartsWith("Settings_HardwareDecoding_", option.ResourceKey);
        }
    }
}