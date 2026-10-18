using System.Globalization;

namespace Aether.Core.Playback.Updates;

/// <summary>
/// A release version such as <c>1.2.3</c> or <c>1.3.0-beta.2</c>, read from a GitHub release tag (<c>v1.2.3</c>) or the app's own
/// version, and compared by SemVer precedence. Build metadata after <c>+</c> is ignored.
/// </summary>
public sealed record ReleaseVersion(int Major, int Minor, int Patch, string Prerelease) : IComparable<ReleaseVersion>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Reads <c>1.2.3</c>, <c>v1.2.3</c>, <c>1.2</c> or <c>1.2.3-beta.1</c>; false for anything else.</summary>
    public static bool TryParse(string? text, out ReleaseVersion version)
    {
        version = new ReleaseVersion(0, 0, 0, "");
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string rest = text.Trim();
        if (rest.Length > 1 && (rest[0] == 'v' || rest[0] == 'V'))
        {
            rest = rest[1..];
        }
        int plus = rest.IndexOf('+');
        if (plus >= 0)
        {
            rest = rest[..plus];
        }
        string prerelease = "";
        int dash = rest.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = rest[(dash + 1)..];
            rest = rest[..dash];
            if (prerelease.Length == 0 || prerelease.Split('.').Any(p => p.Length == 0 || !p.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
            {
                return false;
            }
        }
        string[] parts = rest.Split('.');
        if (parts.Length is < 2 or > 3 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
        {
            return false;
        }
        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int minor))
        {
            return false;
        }
        int patch = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return false;
        }
        version = new ReleaseVersion(major, minor, patch, prerelease);
        return true;
    }

    public int CompareTo(ReleaseVersion? other)
    {
        if (other is null)
        {
            return 1;
        }
        int core = Major.CompareTo(other.Major);
        if (core == 0)
        {
            core = Minor.CompareTo(other.Minor);
        }
        if (core == 0)
        {
            core = Patch.CompareTo(other.Patch);
        }
        if (core != 0)
        {
            return core;
        }
        // A release outranks its own pre-releases.
        if (!IsPrerelease || !other.IsPrerelease)
        {
            return IsPrerelease == other.IsPrerelease ? 0 : IsPrerelease ? -1 : 1;
        }
        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    /// <summary>True when <paramref name="candidate"/> is a newer release than <paramref name="installed"/>; text that is not a version is never newer.</summary>
    public static bool IsNewer(string? candidate, string? installed) =>
        TryParse(candidate, out ReleaseVersion next) && TryParse(installed, out ReleaseVersion current) && next.CompareTo(current) > 0;

    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";

    private static int ComparePrerelease(string left, string right)
    {
        string[] a = left.Split('.');
        string[] b = right.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool aNumeric = a[i].All(char.IsAsciiDigit);
            bool bNumeric = b[i].All(char.IsAsciiDigit);
            int result;
            if (aNumeric && bNumeric)
            {
                // Compared by length first so a long run of digits cannot overflow.
                string x = a[i].TrimStart('0');
                string y = b[i].TrimStart('0');
                result = x.Length != y.Length ? x.Length.CompareTo(y.Length) : string.CompareOrdinal(x, y);
            }
            else if (aNumeric != bNumeric)
            {
                result = aNumeric ? -1 : 1;
            }
            else
            {
                result = string.CompareOrdinal(a[i], b[i]);
            }
            if (result != 0)
            {
                return Math.Sign(result);
            }
        }
        return a.Length.CompareTo(b.Length);
    }
}
