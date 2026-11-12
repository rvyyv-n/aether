namespace Aether.Core.Playback.Audio;

/// <summary>
/// Night mode (14.3): a compressor takes the edge off explosions, <c>dynaudnorm</c> lifts quiet dialogue, and a limiter
/// keeps what comes out from clipping, so a film can be watched at low volume without riding the volume. It already
/// evens out the level, so the plain normalising filter stays off while it is on.
/// </summary>
public static class NightMode
{
    public const string FilterLabel = "aether-night";

    public const string Filter =
        "@" + FilterLabel + ":lavfi=[acompressor=threshold=-24dB:ratio=4:attack=5:release=250:makeup=2,"
        + "dynaudnorm=f=150:g=15:p=0.9:m=12,alimiter=limit=0.9:level=disabled]";

    public static IReadOnlyList<string[]> Commands(bool on) => on
        ? [["af", "remove", "@" + FilterLabel], ["af", "add", Filter]]
        : [["af", "remove", "@" + FilterLabel]];
}
