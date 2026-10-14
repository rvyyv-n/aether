namespace Aether.Core.Playback.Streams;

public static class YtDlpErrorClassifier
{
    public static StreamFailureReason Classify(string? stderr, int exitCode = 0)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return exitCode == 0 ? StreamFailureReason.None : StreamFailureReason.Unknown;
        }

        string text = stderr.Trim();

        if (ContainsAny(text,
            "private video",
            "this video is private",
            "video is private"))
        {
            return StreamFailureReason.PrivateVideo;
        }

        if (ContainsAny(text,
            "sign in to confirm",
            "sign-in required",
            "sign in required",
            "login required",
            "requires payment",
            "confirm your age",
            "confirm you're not a bot",
            "confirm you’re not a bot",
            "requires authentication"))
        {
            return StreamFailureReason.SignInRequired;
        }

        if (ContainsAny(text,
            "not available in your country",
            "not made this video available",
            "blocked in your country",
            "georestricted",
            "geographic restriction",
            "unavailable in your country"))
        {
            return StreamFailureReason.RegionBlocked;
        }

        if (ContainsAny(text,
            "premieres in",
            "live stream will begin",
            "live event will begin",
            "live stream hasn't started",
            "live stream hasn’t started",
            "scheduled for"))
        {
            return StreamFailureReason.LiveNotStarted;
        }

        if (ContainsAny(text,
            "unable to download webpage",
            "getaddrinfo failed",
            "failed to resolve",
            "could not resolve host",
            "name or service not known",
            "network is unreachable",
            "connection refused",
            "timed out",
            "timed_out",
            "temporary failure in name resolution"))
        {
            return StreamFailureReason.Offline;
        }

        if (ContainsAny(text,
            "unsupported url",
            "is not a valid url",
            "no video formats found",
            "unsupported site"))
        {
            return StreamFailureReason.SiteNotSupported;
        }

        return StreamFailureReason.Unknown;
    }

    private static bool ContainsAny(string text, params string[] phrases)
    {
        foreach (string phrase in phrases)
        {
            if (text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
