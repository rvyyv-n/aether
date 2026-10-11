namespace Aether.Core.Playback;

/// <summary>Picks the file to open from the command line or a file activation.</summary>
public static class LaunchArguments
{
    /// <summary>
    /// The first argument that names a supported media file, or null. Quotes around a path are dropped
    /// and switches (<c>--x</c>, <c>/x</c>) are ignored. Whether the file exists is for the caller to check.
    /// </summary>
    public static string? FirstMediaPath(IEnumerable<string?> arguments)
    {
        foreach (string? argument in arguments)
        {
            string path = argument?.Trim().Trim('"') ?? string.Empty;
            if (path.Length > 0 && MediaFormats.IsSupported(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>
    /// Splits a raw command line (as <c>LaunchActivatedEventArgs.Arguments</c> gives it) into arguments,
    /// treating double-quoted runs as one argument.
    /// </summary>
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return result;
        }
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        bool started = false;
        foreach (char c in commandLine)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    started = false;
                }
            }
            else
            {
                current.Append(c);
                started = true;
            }
        }
        if (started)
        {
            result.Add(current.ToString());
        }
        return result;
    }
}
