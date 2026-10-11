namespace Aether.Core.Playback.Settings;

/// <summary>A problem on one line of the custom mpv options text. <see cref="Line"/> is 1-based.</summary>
public readonly record struct MpvOptionError(int Line, string Message);

/// <summary>
/// Checks the shape of the custom options field: one <c>key=value</c> per line. Blank lines and <c>#</c> comments are
/// allowed. Whether mpv knows the option is a separate check that needs the running engine.
/// </summary>
public static class MpvOptionsValidator
{
    public static IReadOnlyList<MpvOptionError> Validate(string? text)
    {
        var errors = new List<MpvOptionError>();
        if (string.IsNullOrEmpty(text))
        {
            return errors;
        }

        string[] lines = text.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
            {
                errors.Add(new MpvOptionError(i + 1, "Expected key=value"));
                continue;
            }

            string key = line[..eq].Trim();
            if (key.Length == 0)
            {
                errors.Add(new MpvOptionError(i + 1, "Missing option name before ="));
            }
            else if (!IsValidKey(key))
            {
                errors.Add(new MpvOptionError(i + 1, $"Invalid option name: {key}"));
            }
        }
        return errors;
    }

    private static bool IsValidKey(string key)
    {
        foreach (char c in key)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '/'))
            {
                return false;
            }
        }
        return true;
    }
}
