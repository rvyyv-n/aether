using System.Globalization;
using Aether.Core.Playback.Audio;

namespace Aether.Core.Tests;

public class TrackMixTests
{
    [Fact]
    public void Each_Track_Gets_Its_Own_Level_Then_All_Are_Mixed_Without_Halving()
    {
        string graph = TrackMix.Graph([new MixLevel(1, 100, false), new MixLevel(2, 50, false)]);

        Assert.Equal("[aid1]volume@m1=volume=1[a1];[aid2]volume@m2=volume=0.5[a2];[a1][a2]amix=inputs=2:normalize=0[ao]", graph);
    }

    [Fact]
    public void A_Muted_Track_Stays_In_The_Mix_At_Zero_So_Unmuting_Keeps_Its_Level()
    {
        var game = new MixLevel(1, 80, Muted: true);

        Assert.Contains("[aid1]volume@m1=volume=0[a1]", TrackMix.Graph([game, new MixLevel(3, 100, false)]));
        Assert.Equal(80, game.Percent);
    }

    [Fact]
    public void Track_Ids_Need_Not_Be_In_A_Row()
    {
        string graph = TrackMix.Graph([new MixLevel(2, 100, false), new MixLevel(4, 150, false), new MixLevel(7, 0, false)]);

        Assert.Equal(
            "[aid2]volume@m2=volume=1[a2];[aid4]volume@m4=volume=1.5[a4];[aid7]volume@m7=volume=0[a7];[a2][a4][a7]amix=inputs=3:normalize=0[ao]",
            graph);
    }

    [Fact]
    public void Levels_Use_A_Dot_Whatever_The_Culture()
    {
        CultureInfo before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
        try
        {
            Assert.Contains("volume=0.75", TrackMix.Graph([new MixLevel(1, 75, false), new MixLevel(2, 100, false)]));
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
        }
    }

    [Fact]
    public void A_Mix_Needs_Two_Tracks_And_Levels_Stay_In_Range()
    {
        Assert.Throws<ArgumentException>(() => TrackMix.Graph([new MixLevel(1, 100, false)]));
        Assert.Equal(0, MixLevel.Clamp(-5));
        Assert.Equal(TrackMix.MaxPercent, MixLevel.Clamp(999));
        Assert.Equal(120, MixLevel.Clamp(120));
    }

    [Fact]
    public void Everything_Starts_At_Full_And_Unmuted()
    {
        IReadOnlyList<MixLevel> levels = TrackMix.Start([3, 5]);

        Assert.Equal([new MixLevel(3, 100, false), new MixLevel(5, 100, false)], levels);
    }
}
