using Aether.Core.Playback.Audio;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Tests;

public class NightModeTests
{
    [Fact]
    public void Night_Mode_Squeezes_Peaks_Then_Lifts_Quiet_Parts_Then_Caps_The_Output() =>
        Assert.Equal(
            "@aether-night:lavfi=[acompressor=threshold=-24dB:ratio=4:attack=5:release=250:makeup=2,"
            + "dynaudnorm=f=150:g=15:p=0.9:m=12,alimiter=limit=0.9:level=disabled]",
            NightMode.Filter);

    [Fact]
    public void It_Is_Off_By_Default_And_Replaces_Plain_Normalisation_When_On()
    {
        Assert.False(new AetherSettings().NightMode);

        MpvRuntimePlan both = MpvSettingsMap.RuntimePlan(new AetherSettings { NightMode = true, VolumeNormalisation = true }, onBattery: false);

        Assert.True(both.NightMode);
        Assert.False(both.NormaliseVolume);
    }

    [Fact]
    public void Custom_Audio_Filters_Win_Over_Night_Mode() =>
        Assert.False(MpvSettingsMap.RuntimePlan(new AetherSettings { NightMode = true, CustomMpvOptions = "af=lavfi=[bass=g=5]" }, onBattery: false).NightMode);

    [Fact]
    public void Sync_Adds_And_Removes_The_Night_Filter_Once_Per_Change()
    {
        var sent = new List<string[]>();
        var sync = new MpvSettingsSync(sent.Add);
        sync.Apply(new AetherSettings { VolumeNormalisation = true }, false);
        sent.Clear();

        sync.Apply(new AetherSettings { VolumeNormalisation = true, NightMode = true }, false);
        sync.Apply(new AetherSettings { VolumeNormalisation = true, NightMode = true }, false);

        Assert.Contains(["af", "remove", "@aether-norm"], sent);
        Assert.Single(sent, c => c.SequenceEqual(["af", "add", NightMode.Filter]));
        Assert.DoesNotContain(sent, c => c.SequenceEqual(["af", "add", "@aether-norm:dynaudnorm"]));

        sent.Clear();
        sync.Apply(new AetherSettings { VolumeNormalisation = true }, false);
        Assert.Contains(["af", "remove", "@aether-night"], sent);
        Assert.Contains(["af", "add", "@aether-norm:dynaudnorm"], sent);
    }
}
