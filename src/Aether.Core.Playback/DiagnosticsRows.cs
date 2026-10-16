using System.Globalization;

namespace Aether.Core.Playback;

/// <summary>The words the rows use around the numbers, supplied by the app so this library stays free of the language file. <see cref="HardwareFormat"/> holds one <c>{0}</c> for the decoder name.</summary>
public sealed record DiagnosticsWording(string Software, string HardwareFormat, string Playing);

/// <summary>
/// The six lines of the diagnostics panel, as text. Pure: built from a <see cref="PlayerState"/> so the
/// wording is tested without a window. A value mpv has not reported reads as <see cref="Unavailable"/>.
/// </summary>
public sealed record DiagnosticsRows(
    string Decoder,
    string Video,
    string FrameRate,
    string Display,
    string Dropped,
    string Cache)
{
    public const string Unavailable = "—";

    public static DiagnosticsRows From(in PlayerState state, DiagnosticsWording wording) => new(
        FormatDecoder(state.Decoder, wording),
        FormatVideo(state.VideoFormat, state.CodedWidth, state.CodedHeight),
        FormatFrameRate(state.FramesPerSecond, state.EstimatedFps, wording),
        state.DisplayFps > 0 ? Number(state.DisplayFps, "0.##") + " Hz" : Unavailable,
        $"{state.DecoderDroppedFrames.ToString(CultureInfo.InvariantCulture)} · {state.DroppedFrames.ToString(CultureInfo.InvariantCulture)}",
        state.CacheDuration >= 0 ? Number(state.CacheDuration, "0.0") + " s" : Unavailable);

    /// <summary>Plain text for the clipboard: the title, then "label: value" per row. <paramref name="labels"/> follow the row order.</summary>
    public string ToText(string title, IReadOnlyList<string> labels)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(labels);
        if (labels.Count != 6)
        {
            throw new ArgumentException("Expected one label per row.", nameof(labels));
        }

        return string.Join(Environment.NewLine,
            title,
            $"{labels[0]}: {Decoder}",
            $"{labels[1]}: {Video}",
            $"{labels[2]}: {FrameRate}",
            $"{labels[3]}: {Display}",
            $"{labels[4]}: {Dropped}",
            $"{labels[5]}: {Cache}");
    }

    private static string FormatDecoder(string? decoder, DiagnosticsWording wording) => decoder switch
    {
        null or "" => Unavailable,
        "no" => wording.Software,
        _ => string.Format(CultureInfo.CurrentCulture, wording.HardwareFormat, decoder),
    };

    private static string FormatVideo(string? format, int width, int height)
    {
        string size = width > 0 && height > 0 ? $"{width}×{height}" : "";
        if (string.IsNullOrEmpty(format))
        {
            return size.Length > 0 ? size : Unavailable;
        }
        return size.Length > 0 ? $"{format} · {size}" : format;
    }

    /// <summary>The file's own rate, then what is actually being shown when mpv has measured it.</summary>
    private static string FormatFrameRate(double container, double estimated, DiagnosticsWording wording)
    {
        if (container > 0 && estimated > 0)
        {
            return $"{Number(container, "0.###")} fps · {Number(estimated, "0.#")} {wording.Playing}";
        }
        if (container > 0)
        {
            return Number(container, "0.###") + " fps";
        }
        return estimated > 0 ? Number(estimated, "0.#") + " fps" : Unavailable;
    }

    private static string Number(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);
}
