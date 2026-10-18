namespace Aether.Core.Playback.Streams;

/// <summary>Orders yt-dlp's date versions ("2026.07.04", with a ".1" style suffix for same-day fixes) and picks the copy to run.</summary>
public static class YtDlpVersions
{
    /// <summary>Negative when <paramref name="a"/> is older than <paramref name="b"/>. Text that is not a version sorts oldest.</summary>
    public static int Compare(string? a, string? b)
    {
        int[]? left = Parse(a);
        int[]? right = Parse(b);
        if (left is null || right is null)
        {
            return (left is null ? 0 : 1) - (right is null ? 0 : 1);
        }
        for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
        {
            int difference = (i < left.Length ? left[i] : 0).CompareTo(i < right.Length ? right[i] : 0);
            if (difference != 0)
            {
                return difference;
            }
        }
        return 0;
    }

    /// <summary>
    /// The newest of the downloaded, bundled and PATH copies; ties go to the earlier in that order so the copy Aether
    /// manages wins over one it does not. A copy whose version is unknown loses to any that is known.
    /// </summary>
    public static string? Choose(IReadOnlyList<(string Path, string? Version)> candidates)
    {
        (string Path, string? Version)? best = null;
        foreach ((string Path, string? Version) candidate in candidates)
        {
            if (best is null || Compare(candidate.Version, best.Value.Version) > 0)
            {
                best = candidate;
            }
        }
        return best?.Path;
    }

    private static int[]? Parse(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return null;
        }
        string[] parts = version.Trim().Split('.');
        var numbers = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0)
            {
                return null;
            }
        }
        return numbers;
    }
}
