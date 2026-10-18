using System.Security.Cryptography;

namespace Aether.Core.Playback.Streams;

/// <summary>Checks a downloaded yt-dlp.exe against the SHA-256 the release publishes in its SHA2-256SUMS file.</summary>
public static class YtDlpDownloadCheck
{
    /// <summary>The hash listed for <paramref name="fileName"/> in a "hash  name" sums file, lowercase; null when it has no such line.</summary>
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (string line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // A leading '*' marks binary mode in sums files.
            if (parts.Length == 2 && parts[1].TrimStart('*') == fileName && IsSha256Hex(parts[0]))
            {
                return parts[0].ToLowerInvariant();
            }
        }
        return null;
    }

    public static async Task<string> HashAsync(Stream content, CancellationToken cancellationToken = default) =>
        Convert.ToHexStringLower(await SHA256.HashDataAsync(content, cancellationToken).ConfigureAwait(false));

    /// <summary>True when <paramref name="actual"/> equals <paramref name="expected"/>, ignoring case.</summary>
    public static bool Matches(string? expected, string actual) =>
        expected is not null && string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);

    private static bool IsSha256Hex(string text) => text.Length == 64 && text.All(char.IsAsciiHexDigit);
}
