namespace Aether.Core.Playback;

/// <summary>The containers the player accepts from drag and drop and the open dialog.</summary>
public static class MediaFormats
{
    /// <summary>Lower-case extensions with the leading dot, in display order.</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".mp4", ".mkv", ".mov", ".webm"];

    /// <summary>True if <paramref name="path"/> ends in a supported extension (case-insensitive).</summary>
    public static bool IsSupported(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }
        ReadOnlySpan<char> extension = Path.GetExtension(path.AsSpan());
        foreach (string supported in Extensions)
        {
            if (extension.Equals(supported, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The extension without its dot, upper-cased (<c>MKV</c>); empty when there is none.</summary>
    public static string Label(string? path)
    {
        ReadOnlySpan<char> extension = Path.GetExtension(path.AsSpan());
        return extension.Length > 1 ? extension[1..].ToString().ToUpperInvariant() : string.Empty;
    }
}
