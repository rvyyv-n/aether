using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Aether.Core.Playback.Audio;

/// <summary>
/// A 10-band equalizer setting (14.1): a preamp and a gain per band of <see cref="Equalizer.Frequencies"/>, each within
/// ±<see cref="Equalizer.MaxGain"/> dB. Saved as <c>preamp;g31,g62,…,g16k</c> with a dot for decimals.
/// </summary>
public sealed class EqualizerCurve : IEquatable<EqualizerCurve>
{
    private readonly double[] _gains;

    public EqualizerCurve(double preamp, IReadOnlyList<double> gains)
    {
        ArgumentNullException.ThrowIfNull(gains);
        ArgumentOutOfRangeException.ThrowIfNotEqual(gains.Count, Equalizer.Frequencies.Count, nameof(gains));
        if (!InRange(preamp) || !gains.All(InRange))
        {
            throw new ArgumentOutOfRangeException(nameof(gains), $"gains are within ±{Equalizer.MaxGain} dB");
        }
        Preamp = preamp;
        _gains = [.. gains];
    }

    public static EqualizerCurve Flat { get; } = new(0, new double[10]);

    public double Preamp { get; }

    public IReadOnlyList<double> Gains => _gains;

    public bool IsFlat => Preamp == 0 && _gains.All(g => g == 0);

    public static bool TryParse(string? text, [NotNullWhen(true)] out EqualizerCurve? curve)
    {
        curve = null;
        string[] halves = (text ?? "").Split(';');
        if (halves.Length != 2 || !TryNumber(halves[0], out double preamp))
        {
            return false;
        }
        string[] parts = halves[1].Split(',');
        var gains = new double[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!TryNumber(parts[i], out gains[i]))
            {
                return false;
            }
        }
        if (gains.Length != Equalizer.Frequencies.Count || !InRange(preamp) || !gains.All(InRange))
        {
            return false;
        }
        curve = new EqualizerCurve(preamp, gains);
        return true;
    }

    public EqualizerCurve WithPreamp(double preamp) => new(preamp, _gains);

    public EqualizerCurve WithGain(int band, double gain)
    {
        double[] gains = [.. _gains];
        gains[band] = gain;
        return new EqualizerCurve(Preamp, gains);
    }

    public override string ToString() => $"{Equalizer.Number(Preamp)};{string.Join(',', _gains.Select(Equalizer.Number))}";

    public bool Equals(EqualizerCurve? other) => other is not null && Preamp == other.Preamp && _gains.SequenceEqual(other._gains);

    public override bool Equals(object? obj) => Equals(obj as EqualizerCurve);

    public override int GetHashCode() => HashCode.Combine(Preamp, _gains.Sum());

    private static bool InRange(double value) => double.IsFinite(value) && Math.Abs(value) <= Equalizer.MaxGain;

    private static bool TryNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
}

/// <summary>
/// The equalizer's mpv filter: a preamp and ten peaking bands an octave wide, in one labelled <c>lavfi</c> graph. Every
/// band is always in the graph, so moving a slider is one <c>af-command</c> to that band and the sound never drops out.
/// </summary>
public static class Equalizer
{
    public const string FilterLabel = "aether-eq";

    public const double MaxGain = 12;

    public static IReadOnlyList<int> Frequencies { get; } = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public static string Filter(EqualizerCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        IEnumerable<string> bands = Frequencies.Select((f, i) =>
            $"equalizer@b{i}=f={f.ToString(CultureInfo.InvariantCulture)}:t=o:w=1:g={Number(curve.Gains[i])}");
        return $"@{FilterLabel}:lavfi=[volume@pre=volume={Number(curve.Preamp)}dB,{string.Join(',', bands)}]";
    }

    /// <summary>What takes the running core from <paramref name="from"/> to <paramref name="to"/>; null is off.</summary>
    public static IReadOnlyList<string[]> Commands(EqualizerCurve? from, EqualizerCurve? to)
    {
        if (to is null)
        {
            return from is null ? [] : [["af", "remove", "@" + FilterLabel]];
        }
        if (from is null)
        {
            return [["af", "remove", "@" + FilterLabel], ["af", "add", Filter(to)]];
        }
        var commands = new List<string[]>();
        if (from.Preamp != to.Preamp)
        {
            commands.Add(["af-command", FilterLabel, "volume", Number(to.Preamp) + "dB", "volume@pre"]);
        }
        for (int i = 0; i < Frequencies.Count; i++)
        {
            if (from.Gains[i] != to.Gains[i])
            {
                commands.Add(["af-command", FilterLabel, "g", Number(to.Gains[i]), $"equalizer@b{i}"]);
            }
        }
        return commands;
    }

    internal static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>A named curve the equalizer offers; <see cref="Key"/> is also the string key's suffix.</summary>
public sealed record EqualizerPreset(string Key, EqualizerCurve Curve);

/// <summary>
/// The curated curves (14.2). Each lowers the preamp so its loudest band stays within 3 dB of the original level, which
/// keeps loud scenes from clipping.
/// </summary>
public static class EqualizerPresets
{
    public const string Custom = "custom";

    public static IReadOnlyList<EqualizerPreset> All { get; } =
    [
        new("flat", EqualizerCurve.Flat),
        new("bass", new EqualizerCurve(-3, [6, 5, 4, 2, 0, 0, 0, 0, 0, 0])),
        new("vocal", new EqualizerCurve(-1, [-2, -2, -1, 0, 2, 4, 4, 3, 1, 0])),
        new("treble", new EqualizerCurve(-3, [0, 0, 0, 0, 0, 1, 2, 4, 5, 6])),
        new("rock", new EqualizerCurve(-1, [4, 3, 2, 0, -1, -1, 0, 2, 3, 4])),
        new("electronic", new EqualizerCurve(-2, [5, 4, 1, 0, -2, 0, 1, 3, 4, 4])),
        new("cinema", new EqualizerCurve(-2, [5, 4, 2, 0, -1, 1, 3, 3, 2, 1])),
    ];

    public static EqualizerPreset? Find(string key) => All.FirstOrDefault(p => p.Key == key);

    /// <summary>The preset <paramref name="curve"/> is, or <see cref="Custom"/>.</summary>
    public static string KeyOf(EqualizerCurve curve) => All.FirstOrDefault(p => p.Curve.Equals(curve))?.Key ?? Custom;
}
