using System.Collections.Frozen;

namespace Aether.Core.Playback.Settings;

/// <summary>The kind of validation failure encountered on a line of custom mpv options.</summary>
public enum MpvOptionErrorKind
{
    MissingEquals,
    MissingName,
    InvalidName,
    Reserved,
    Duplicate,
}

/// <summary>A problem on one line of the custom mpv options text. <see cref="Line"/> is 1-based.</summary>
public readonly record struct MpvOptionError(int Line, MpvOptionErrorKind Kind, string? Key = null);

/// <summary>
/// Checks the shape of the custom options field: one <c>key=value</c> per line. Blank lines and <c>#</c> comments are
/// allowed. Whether mpv knows the option is a separate check that needs the running engine.
/// </summary>
public static class MpvOptionsValidator
{
    private static readonly FrozenSet<string> ReservedKeys =
        new[]
        {
            "vo",
            "wid",
            "gpu-context",
            "gpu-api",
            "hwdec",
            "config",
            "config-dir",
            "include",
            "input-ipc-server",
            "script",
            "scripts",
            "load-scripts",
        }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<MpvOptionError> Validate(string? text)
    {
        var errors = new List<MpvOptionError>();
        if (string.IsNullOrEmpty(text))
        {
            return errors;
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                errors.Add(new MpvOptionError(i + 1, MpvOptionErrorKind.MissingEquals));
                continue;
            }

            string rawKey = line[..eq].Trim();
            string key = rawKey;
            if (key.StartsWith("--", StringComparison.Ordinal))
            {
                key = key[2..];
            }

            if (key.Length == 0)
            {
                errors.Add(new MpvOptionError(i + 1, MpvOptionErrorKind.MissingName));
            }
            else if (!IsValidKey(key))
            {
                errors.Add(new MpvOptionError(i + 1, MpvOptionErrorKind.InvalidName, key));
            }
            else if (ReservedKeys.Contains(key))
            {
                errors.Add(new MpvOptionError(i + 1, MpvOptionErrorKind.Reserved, key));
            }
            else if (!seenKeys.Add(key))
            {
                errors.Add(new MpvOptionError(i + 1, MpvOptionErrorKind.Duplicate, key));
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