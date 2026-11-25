namespace Aether.Core.Playback;

/// <summary>Optional controls that can appear on the expanded player bar (12.3). Open, Play/Pause, Volume and Full screen stay pinned.</summary>
public enum TransportBarControl
{
    Speed,
    Quality,
    Skip,
    FrameStep,
    Tracks,
    Loop,
    AlwaysOnTop,
    Chapters,
}

public readonly record struct TransportBarItem(TransportBarControl Control, bool Visible);

/// <summary>
/// Persisted visibility and relative display order for optional controls on the player bar (12.3).
/// Serialised as a comma-separated list of <c>token:1</c> or <c>token:0</c> pairs so settings files stay compact,
/// human-readable and Native-AOT-safe.
/// </summary>
public static class TransportBarLayout
{
    private const int MaxLength = 256;

    public const string DefaultValue = "speed:1,quality:1,skip:1,frame_step:0,tracks:0,loop:0,always_on_top:0,chapters:0";

    private static readonly (TransportBarControl Control, string Token, bool DefaultVisible)[] Defaults =
    [
        (TransportBarControl.Speed, "speed", true),
        (TransportBarControl.Quality, "quality", true),
        (TransportBarControl.Skip, "skip", true),
        (TransportBarControl.FrameStep, "frame_step", false),
        (TransportBarControl.Tracks, "tracks", false),
        (TransportBarControl.Loop, "loop", false),
        (TransportBarControl.AlwaysOnTop, "always_on_top", false),
        (TransportBarControl.Chapters, "chapters", false),
    ];

    public static IReadOnlyList<TransportBarItem> Parse(string? raw)
    {
        var items = new List<TransportBarItem>(Defaults.Length);
        var seen = new HashSet<TransportBarControl>();

        if (!string.IsNullOrWhiteSpace(raw))
        {
            foreach (string segment in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int colon = segment.IndexOf(':');
                string token = colon >= 0 ? segment[..colon].Trim() : segment;
                if (!TryParseToken(token, out TransportBarControl control) || !seen.Add(control))
                {
                    continue;
                }

                bool visible = colon >= 0
                    ? segment[(colon + 1)..].Trim() switch
                    {
                        "1" => true,
                        "0" => false,
                        _ => DefaultVisibleFor(control),
                    }
                    : DefaultVisibleFor(control);
                items.Add(new TransportBarItem(control, visible));
            }
        }

        foreach ((TransportBarControl control, _, bool defaultVisible) in Defaults)
        {
            if (seen.Add(control))
            {
                items.Add(new TransportBarItem(control, defaultVisible));
            }
        }

        return items;
    }

    public static string Format(IReadOnlyList<TransportBarItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var seen = new HashSet<TransportBarControl>();
        var parts = new List<string>(Defaults.Length);

        foreach (TransportBarItem item in items)
        {
            if (seen.Add(item.Control))
            {
                parts.Add(string.Concat(TokenFor(item.Control), ":", item.Visible ? "1" : "0"));
            }
        }

        foreach ((TransportBarControl control, string token, bool defaultVisible) in Defaults)
        {
            if (seen.Add(control))
            {
                parts.Add(string.Concat(token, ":", defaultVisible ? "1" : "0"));
            }
        }

        return string.Join(',', parts);
    }

    public static bool IsValid(string? raw)
    {
        if (raw is null)
        {
            return true;
        }
        if (raw.Length > MaxLength)
        {
            return false;
        }
        foreach (char ch in raw)
        {
            if (char.IsControl(ch))
            {
                return false;
            }
        }
        return true;
    }

    public static string SetVisible(string? raw, TransportBarControl control, bool visible)
    {
        List<TransportBarItem> items = [.. Parse(raw)];
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Control == control)
            {
                items[i] = items[i] with { Visible = visible };
                break;
            }
        }
        return Format(items);
    }

    public static string Move(string? raw, TransportBarControl control, int delta)
    {
        if (delta == 0)
        {
            return Format(Parse(raw));
        }

        List<TransportBarItem> items = [.. Parse(raw)];
        int index = items.FindIndex(item => item.Control == control);
        if (index < 0)
        {
            return Format(items);
        }

        int target = Math.Clamp(index + delta, 0, items.Count - 1);
        if (target == index)
        {
            return Format(items);
        }

        TransportBarItem moved = items[index];
        items.RemoveAt(index);
        items.Insert(target, moved);
        return Format(items);
    }

    public static IReadOnlyList<TransportBarControl> VisibleControls(string? raw)
    {
        IReadOnlyList<TransportBarItem> parsed = Parse(raw);
        var visible = new List<TransportBarControl>(parsed.Count);
        foreach (TransportBarItem item in parsed)
        {
            if (item.Visible)
            {
                visible.Add(item.Control);
            }
        }
        return visible;
    }

    private static bool TryParseToken(string token, out TransportBarControl control)
    {
        foreach ((TransportBarControl candidate, string candidateToken, _) in Defaults)
        {
            if (string.Equals(token, candidateToken, StringComparison.Ordinal))
            {
                control = candidate;
                return true;
            }
        }
        control = default;
        return false;
    }

    private static string TokenFor(TransportBarControl control)
    {
        foreach ((TransportBarControl candidate, string token, _) in Defaults)
        {
            if (candidate == control)
            {
                return token;
            }
        }
        throw new ArgumentOutOfRangeException(nameof(control), control, null);
    }

    private static bool DefaultVisibleFor(TransportBarControl control)
    {
        foreach ((TransportBarControl candidate, _, bool defaultVisible) in Defaults)
        {
            if (candidate == control)
            {
                return defaultVisible;
            }
        }
        return false;
    }
}
