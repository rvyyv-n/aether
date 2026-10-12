namespace Aether.Core.Playback;

/// <summary>
/// Orders names the way Explorer does: runs of digits compare by value ("ep2" before "ep10") and everything
/// else compares ignoring case. Managed so it behaves the same everywhere and needs no native call.
/// </summary>
public sealed class NaturalOrder : IComparer<string>
{
    public static NaturalOrder Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        if (x is null)
        {
            return -1;
        }
        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int startX = i, startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i]))
                {
                    i++;
                }
                while (j < y.Length && char.IsAsciiDigit(y[j]))
                {
                    j++;
                }
                int byValue = CompareNumbers(x.AsSpan(startX, i - startX), y.AsSpan(startY, j - startY));
                if (byValue != 0)
                {
                    return byValue;
                }
                continue;
            }
            int byChar = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (byChar != 0)
            {
                return byChar;
            }
            i++;
            j++;
        }
        int byLength = (x.Length - i).CompareTo(y.Length - j);
        return byLength != 0 ? byLength : string.CompareOrdinal(x, y);
    }

    // Digit runs can be longer than any integer type, so compare them as text without leading zeros.
    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        ReadOnlySpan<char> trimmedA = a.TrimStart('0');
        ReadOnlySpan<char> trimmedB = b.TrimStart('0');
        int byLength = trimmedA.Length.CompareTo(trimmedB.Length);
        return byLength != 0 ? byLength : trimmedA.CompareTo(trimmedB, StringComparison.Ordinal);
    }
}
