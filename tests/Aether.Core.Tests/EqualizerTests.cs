using System.Globalization;
using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Tests;

public class EqualizerTests
{
    private static readonly double[] Flat = new double[10];

    private sealed class NullStorage : ISettingsStorage
    {
        public string? Read() => null;
        public void Write(string content) { }
    }

    [Fact]
    public void Ten_Standard_Bands_From_31_Hz_To_16_kHz() =>
        Assert.Equal([31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000], Equalizer.Frequencies);

    [Fact]
    public void A_Curve_Is_Saved_As_Preamp_Then_The_Bands_And_Read_Back()
    {
        var curve = new EqualizerCurve(-3, [6, 4.5, 2, 0, 0, 0, -1.5, 0, 3, 12]);
        string text = curve.ToString();

        Assert.Equal("-3;6,4.5,2,0,0,0,-1.5,0,3,12", text);
        Assert.True(EqualizerCurve.TryParse(text, out EqualizerCurve? back));
        Assert.Equal(curve, back);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("0;0,0,0")]
    [InlineData("0;0,0,0,0,0,0,0,0,0,0,0")]
    [InlineData("0;0,0,0,0,0,0,0,0,0,12.5")]
    [InlineData("-13;0,0,0,0,0,0,0,0,0,0")]
    [InlineData("x;0,0,0,0,0,0,0,0,0,0")]
    [InlineData("0;0,0,0,0,0,NaN,0,0,0,0")]
    public void Anything_But_Ten_Bands_Within_12_dB_Is_Refused(string text) =>
        Assert.False(EqualizerCurve.TryParse(text, out _));

    [Fact]
    public void The_Filter_Holds_Every_Band_So_Each_Can_Be_Moved_Live()
    {
        string filter = Equalizer.Filter(new EqualizerCurve(-2.5, [3, 0, 0, 0, 0, 0, 0, 0, 0, -4]));

        Assert.Equal(
            "@aether-eq:lavfi=[volume@pre=volume=-2.5dB,"
            + "equalizer@b0=f=31:t=o:w=1:g=3,equalizer@b1=f=62:t=o:w=1:g=0,equalizer@b2=f=125:t=o:w=1:g=0,"
            + "equalizer@b3=f=250:t=o:w=1:g=0,equalizer@b4=f=500:t=o:w=1:g=0,equalizer@b5=f=1000:t=o:w=1:g=0,"
            + "equalizer@b6=f=2000:t=o:w=1:g=0,equalizer@b7=f=4000:t=o:w=1:g=0,equalizer@b8=f=8000:t=o:w=1:g=0,"
            + "equalizer@b9=f=16000:t=o:w=1:g=-4]",
            filter);
    }

    [Fact]
    public void Numbers_Use_A_Dot_Whatever_The_Culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var curve = new EqualizerCurve(-1.5, [0.5, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
            Assert.Contains("volume=-1.5dB", Equalizer.Filter(curve));
            Assert.Equal("-1.5;0.5,0,0,0,0,0,0,0,0,0", curve.ToString());
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void Turning_On_Adds_The_Filter_And_Off_Removes_It()
    {
        var curve = new EqualizerCurve(0, Flat);

        IReadOnlyList<string[]> on = Equalizer.Commands(null, curve);
        Assert.Equal(["af", "remove", "@aether-eq"], on[0]);
        Assert.Equal(["af", "add", Equalizer.Filter(curve)], on[1]);
        Assert.Equal([["af", "remove", "@aether-eq"]], Equalizer.Commands(curve, null));
        Assert.Empty(Equalizer.Commands(null, null));
    }

    [Fact]
    public void Moving_A_Band_Sends_Only_That_Band_To_The_Running_Filter()
    {
        var before = new EqualizerCurve(0, Flat);
        var after = new EqualizerCurve(-3, [0, 0, 5.5, 0, 0, 0, 0, 0, 0, 0]);

        IReadOnlyList<string[]> commands = Equalizer.Commands(before, after);

        Assert.Equal(
            [["af-command", "aether-eq", "volume", "-3dB", "volume@pre"], ["af-command", "aether-eq", "g", "5.5", "equalizer@b2"]],
            commands);
        Assert.Empty(Equalizer.Commands(after, after));
    }

    [Fact]
    public void Presets_Cover_The_Roadmap_List_And_A_Curve_Names_Its_Preset()
    {
        Assert.Equal(["flat", "bass", "vocal", "treble", "rock", "electronic", "cinema"], EqualizerPresets.All.Select(p => p.Key));
        Assert.All(EqualizerPresets.All, p => Assert.True(EqualizerCurve.TryParse(p.Curve.ToString(), out _)));
        Assert.Equal("bass", EqualizerPresets.KeyOf(EqualizerPresets.Find("bass")!.Curve));
        Assert.Equal("flat", EqualizerPresets.KeyOf(new EqualizerCurve(0, Flat)));
        Assert.Equal("custom", EqualizerPresets.KeyOf(new EqualizerCurve(0, [1, 0, 0, 0, 0, 0, 0, 0, 0, 0])));
    }

    [Fact]
    public void Presets_Leave_Headroom_For_Their_Boost()
    {
        foreach (EqualizerPreset preset in EqualizerPresets.All)
        {
            Assert.True(preset.Curve.Preamp + preset.Curve.Gains.Max() <= 3, preset.Key);
        }
    }

    [Fact]
    public void Settings_Store_A_Valid_Curve_And_Refuse_A_Broken_One()
    {
        var store = new SettingsStore(new NullStorage());

        Assert.False(store.Current.EqualizerEnabled);
        Assert.Equal("0;0,0,0,0,0,0,0,0,0,0", store.Current.EqualizerCurve);
        Assert.True(store.Set(SettingKeys.EqualizerCurve, "2;1,1,1,1,1,1,1,1,1,1"));
        Assert.False(store.Set(SettingKeys.EqualizerCurve, "2;1,1"));
        Assert.Equal("2;1,1,1,1,1,1,1,1,1,1", store.Current.EqualizerCurve);
    }

    [Fact]
    public void Sync_Adds_The_Filter_Then_Moves_Bands_Live_And_Removes_It()
    {
        var sent = new List<string[]>();
        var sync = new MpvSettingsSync(sent.Add);
        var settings = new AetherSettings();

        sync.Apply(settings, false);
        Assert.DoesNotContain(sent, c => c.Contains("@aether-eq"));

        sync.Apply(settings = settings with { EqualizerEnabled = true }, false);
        Assert.Equal(["af", "add", Equalizer.Filter(new EqualizerCurve(0, Flat))], sent[^1]);

        sent.Clear();
        sync.Apply(settings = settings with { EqualizerCurve = "0;0,0,0,0,0,0,0,0,0,2" }, false);
        Assert.Equal([["af-command", "aether-eq", "g", "2", "equalizer@b9"]], sent);

        sync.Apply(settings with { EqualizerEnabled = false }, false);
        Assert.Equal(["af", "remove", "@aether-eq"], sent[^1]);
    }

    [Fact]
    public void A_New_File_Gets_The_Curve_The_Sliders_Moved_To()
    {
        var sent = new List<string[]>();
        var sync = new MpvSettingsSync(sent.Add);
        var settings = new AetherSettings { EqualizerEnabled = true };
        sync.Apply(settings, false);
        sync.PrepareForFile();
        sent.Clear();

        // A live move changes the running filter only; mpv builds the next file's filter from the saved entry.
        sync.Apply(settings with { EqualizerCurve = "0;4,0,0,0,0,0,0,0,0,0" }, false);
        sync.PrepareForFile();

        Assert.Equal(["af", "remove", "@aether-eq"], sent[^2]);
        Assert.Equal(["af", "add", Equalizer.Filter(new EqualizerCurve(0, [4, 0, 0, 0, 0, 0, 0, 0, 0, 0]))], sent[^1]);
        int count = sent.Count;
        sync.PrepareForFile();
        Assert.Equal(count, sent.Count);
    }

    [Fact]
    public void Custom_Audio_Filters_Win_Over_The_Equalizer()
    {
        MpvRuntimePlan plan = MpvSettingsMap.RuntimePlan(
            new AetherSettings { EqualizerEnabled = true, CustomMpvOptions = "af=lavfi=[bass=g=5]" }, onBattery: false);

        Assert.Null(plan.Equalizer);
    }
}
