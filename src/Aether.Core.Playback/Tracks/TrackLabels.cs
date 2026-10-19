using System.Globalization;
using System.Text.RegularExpressions;

namespace Aether.Core.Playback.Tracks;

/// <summary>Labels for the tracks flyout (redline 9.2): language · title · channels, or language · title · SDH or Forced.</summary>
public static partial class TrackLabels
{
    private const string Separator = " · ";

    // Container language tags are usually ISO 639-2, which CultureInfo only knows for some languages, and the
    // bibliographic spellings (fre, ger, chi...) for none of them.
    private static readonly Dictionary<string, string> Bibliographic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["alb"] = "sq", ["arm"] = "hy", ["baq"] = "eu", ["bur"] = "my", ["chi"] = "zh", ["cze"] = "cs",
        ["dut"] = "nl", ["fre"] = "fr", ["geo"] = "ka", ["ger"] = "de", ["gre"] = "el", ["ice"] = "is",
        ["mac"] = "mk", ["mao"] = "mi", ["may"] = "ms", ["per"] = "fa", ["rum"] = "ro", ["slo"] = "sk",
        ["tib"] = "bo", ["wel"] = "cy",
    };

    private static readonly Lazy<Dictionary<string, CultureInfo>> ByThreeLetterCode = new(() =>
    {
        var map = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (CultureInfo culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.ThreeLetterISOLanguageName is { Length: 3 } code)
            {
                map.TryAdd(code, culture);
            }
        }
        return map;
    });

    /// <summary>
    /// The row text for <paramref name="track"/>. An external subtitle shows its file name; a track without a language
    /// is "Track N" after its id. <paramref name="trackText"/> is the localised "Track {0}", <paramref name="sdh"/> and
    /// <paramref name="forced"/> the localised flags. A track that comes from a web address would show the address's
    /// last part ("videoplayback?expire=..."), so it is named <paramref name="linkTitle"/>, the title of the video, instead.
    /// </summary>
    public static string Format(MediaTrack track, string trackText, string sdh, string forced, string? linkTitle = null)
    {
        if (track.ExternalFile is { } file)
        {
            if (!IsWebAddress(file))
            {
                return Path.GetFileName(file);
            }
            if (!string.IsNullOrWhiteSpace(linkTitle))
            {
                return linkTitle.Trim();
            }
        }

        string? language = LanguageName(track.Language);
        var parts = new List<string>(3) { language ?? string.Format(CultureInfo.CurrentCulture, trackText, track.Id) };

        string? flag = track.Kind == TrackKind.Subtitle
            ? (track.HearingImpaired ? sdh : track.Forced ? forced : null)
            : null;
        if (AddsInformation(track.Title, language, track.Language, flag))
        {
            parts.Add(track.Title!);
        }
        if (track.Kind == TrackKind.Audio && ChannelLayout(track.ChannelLayout, track.ChannelCount) is { } layout)
        {
            parts.Add(layout);
        }
        else if (flag is not null)
        {
            parts.Add(flag);
        }
        return string.Join(Separator, parts);
    }

    /// <summary>The display name of a language tag, or null when there is none or nothing recognises it.</summary>
    public static string? LanguageName(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag) || tag.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        tag = tag.Trim();
        CultureInfo? culture = null;
        if (tag.Length == 3 && Bibliographic.TryGetValue(tag, out string? twoLetter))
        {
            tag = twoLetter;
        }
        if (tag.Length == 3 && ByThreeLetterCode.Value.TryGetValue(tag, out CultureInfo? known))
        {
            culture = known;
        }
        else
        {
            try
            {
                culture = CultureInfo.GetCultureInfo(tag.Replace('_', '-'));
            }
            catch (CultureNotFoundException)
            {
            }
        }
        if (culture is null || culture.Equals(CultureInfo.InvariantCulture))
        {
            return tag; // the tag itself still beats nothing
        }
        return culture.DisplayName;
    }

    /// <summary><c>demux-channels</c> as a layout: mono 1.0, stereo 2.0, 5.1(side) 5.1. Null when the count is unknown too.</summary>
    public static string? ChannelLayout(string? layout, int channelCount)
    {
        if (layout is not null)
        {
            string name = layout.Trim().ToLowerInvariant();
            switch (name)
            {
                case "mono": return "1.0";
                case "stereo": return "2.0";
                case "quad": return "4.0";
                case "hexagonal": return "6.0";
                case "octagonal": return "8.0";
            }
            Match match = LayoutNumbers().Match(name);
            if (match.Success)
            {
                return match.Groups[2].Success ? $"{match.Groups[1].Value}.{match.Groups[2].Value}" : $"{match.Groups[1].Value}.0";
            }
        }
        return channelCount switch
        {
            1 => "1.0",
            2 => "2.0",
            6 => "5.1",
            8 => "7.1",
            _ => null,
        };
    }

    private static bool IsWebAddress(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>A title says something extra unless it just repeats the language, its tag or the SDH / Forced flag.</summary>
    private static bool AddsInformation(string? title, string? language, string? tag, string? flag) =>
        !string.IsNullOrWhiteSpace(title)
        && !title.Equals(language, StringComparison.OrdinalIgnoreCase)
        && !title.Equals(tag, StringComparison.OrdinalIgnoreCase)
        && !title.Equals(flag, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^(\d)(?:\.(\d))?")]
    private static partial Regex LayoutNumbers();
}
