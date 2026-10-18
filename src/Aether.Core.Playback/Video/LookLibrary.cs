using System.Globalization;

namespace Aether.Core.Playback.Video;

/// <summary>
/// Where the grades live: seven built-in 3D LUTs next to the app and the user's own in <c>%LOCALAPPDATA%\Aether\Looks</c>.
/// A grade is named by an id: a built-in one ("warm") or "user:" and the file name ("user:My look.cube").
/// </summary>
public sealed class LookLibrary(string builtInDirectory, string userDirectory)
{
    public const string UserPrefix = "user:";
    private const string Extension = ".cube";
    private const int MinLutSize = 2;
    private const int MaxLutSize = 65;
    private const int HeaderLines = 200;

    /// <summary>The ids of the built-in grades, in the order the combo lists them (after None).</summary>
    public static IReadOnlyList<string> BuiltIn { get; } = ["warm", "cool", "vivid", "teal-orange", "faded-film", "black-white"];

    public static string DefaultUserDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether", "Looks");

    public static string DefaultBuiltInDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "Assets", "Looks");

    public string UserDirectory { get; } = userDirectory;

    /// <summary>The user's looks by file name, A to Z, as grade ids. Re-read on every call, so the panel sees files added by hand.</summary>
    public IReadOnlyList<string> UserGrades()
    {
        if (!Directory.Exists(UserDirectory))
        {
            return [];
        }
        try
        {
            return [.. Directory.EnumerateFiles(UserDirectory, "*" + Extension)
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(IsUsableFileName)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .Select(name => UserPrefix + name)];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The name shown for a user grade: the file name without its extension.</summary>
    public static string DisplayName(string grade) =>
        Path.GetFileNameWithoutExtension(grade.StartsWith(UserPrefix, StringComparison.Ordinal) ? grade[UserPrefix.Length..] : grade);

    /// <summary>The file for a grade, or null for None, an unknown id or a file that has gone.</summary>
    public string? ResolvePath(string grade)
    {
        string? path = null;
        if (BuiltIn.Contains(grade))
        {
            path = Path.Combine(builtInDirectory, grade + Extension);
        }
        else if (grade.StartsWith(UserPrefix, StringComparison.Ordinal))
        {
            string name = grade[UserPrefix.Length..];
            if (IsUsableFileName(name) && name == Path.GetFileName(name))
            {
                path = Path.Combine(UserDirectory, name);
            }
        }
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Checks the file is a 3D LUT and copies it into the user's folder, adding " (2)" and so on on a name clash. Returns
    /// the new grade id, or null when the file is not a usable .cube or could not be copied.
    /// </summary>
    public string? TryImport(string sourcePath)
    {
        if (!string.Equals(Path.GetExtension(sourcePath), Extension, StringComparison.OrdinalIgnoreCase) || !IsValidCube(sourcePath))
        {
            return null;
        }
        try
        {
            Directory.CreateDirectory(UserDirectory);
            string stem = SafeName(Path.GetFileNameWithoutExtension(sourcePath));
            string name = stem + Extension;
            for (int copy = 2; File.Exists(Path.Combine(UserDirectory, name)); copy++)
            {
                name = string.Create(CultureInfo.InvariantCulture, $"{stem} ({copy}){Extension}");
            }
            File.Copy(sourcePath, Path.Combine(UserDirectory, name));
            return UserPrefix + name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True when the header declares <c>LUT_3D_SIZE</c> from 2 to 65 (a 1D LUT has no such line).</summary>
    public static bool IsValidCube(string path)
    {
        try
        {
            foreach (string raw in File.ReadLines(path).Take(HeaderLines))
            {
                string line = raw.Trim();
                if (!line.StartsWith("LUT_3D_SIZE", StringComparison.Ordinal))
                {
                    continue;
                }
                string[] fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                return fields.Length == 2
                    && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int size)
                    && size is >= MinLutSize and <= MaxLutSize;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    // mpv's bracket syntax for the lavfi chain would be broken by these, so such files are neither listed nor kept.
    private static bool IsUsableFileName(string name) => name.AsSpan().IndexOfAny('[', ']') < 0;

    private static string SafeName(string stem) => stem.Replace('[', '(').Replace(']', ')');
}
