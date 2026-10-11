namespace Aether.Core.Playback;

/// <summary>What the Open URL dialog accepts: text that starts with http:// or https:// (2.5).</summary>
public static class WebLink
{
    public static bool IsValid(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }
        string trimmed = text.Trim();
        return (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            && Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            && uri.Host.Length > 0;
    }

    /// <summary>The site as the shell names it ("youtube.com"), or null when the text is not a link.</summary>
    public static string? Host(string? text)
    {
        if (!IsValid(text) || !Uri.TryCreate(text!.Trim(), UriKind.Absolute, out Uri? uri))
        {
            return null;
        }
        string host = uri.Host;
        return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
    }
}
