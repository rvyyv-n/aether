namespace Aether.Core.Playback.Diagnostics;

/// <summary>
/// Command-line configuration for in-app visual snapshot capture (<c>--visual-audit &lt;dir&gt; [--video &lt;path&gt;] [--shots a,b]</c>).
/// </summary>
public sealed record VisualAuditOptions(
    string OutputDirectory,
    string? VideoPath,
    IReadOnlySet<string>? ShotFilter)
{
    public bool MatchesShot(string shotId) =>
        ShotFilter is null || ShotFilter.Count == 0 || ShotFilter.Contains(shotId);

    /// <summary>
    /// Parses visual audit flags from command-line arguments (excluding argv[0]). Returns null when
    /// <c>--visual-audit</c> is not present or has no output directory.
    /// </summary>
    public static VisualAuditOptions? TryParse(IEnumerable<string?> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        string[] list = arguments
            .Where(static a => !string.IsNullOrWhiteSpace(a))
            .Select(static a => a!.Trim().Trim('"'))
            .ToArray();

        string? outputDir = null;
        string? videoPath = null;
        HashSet<string>? shots = null;

        for (int i = 0; i < list.Length; i++)
        {
            string arg = list[i];
            if (TryReadValue(arg, "--visual-audit", list, ref i, out string? dir))
            {
                outputDir = dir;
            }
            else if (TryReadValue(arg, "--video", list, ref i, out string? video))
            {
                videoPath = video;
            }
            else if (TryReadValue(arg, "--shots", list, ref i, out string? shotCsv))
            {
                shots ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string token in shotCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    shots.Add(token);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(outputDir))
        {
            return null;
        }

        return new VisualAuditOptions(
            Path.GetFullPath(outputDir),
            string.IsNullOrWhiteSpace(videoPath) ? null : Path.GetFullPath(videoPath),
            shots);
    }

    private static bool TryReadValue(string arg, string flag, string[] list, ref int index, out string value)
    {
        if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
        {
            if (index + 1 < list.Length && !list[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = list[++index];
                return !string.IsNullOrWhiteSpace(value);
            }
            value = string.Empty;
            return false;
        }

        string prefix = flag + "=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = arg[prefix.Length..].Trim().Trim('"');
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }
}
