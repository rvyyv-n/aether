using System.Globalization;
using System.Text;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Streams;

public static class YtDlpArgumentsBuilder
{
    public static IReadOnlyList<string> BuildArgumentList(string url, StreamResolutionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(options);

        var args = new List<string>(12)
        {
            "--dump-json",
            "--no-playlist",
            "--no-warnings",
            "--socket-timeout",
            SocketTimeoutSeconds(options.Timeout).ToString(CultureInfo.InvariantCulture),
            "--extractor-args",
            "youtube:skip=translated_subs",
        };

        string format = !string.IsNullOrWhiteSpace(options.FormatSelectorOverride)
            ? options.FormatSelectorOverride
            : MpvSettingsMap.YtdlFormat(options.StreamingQuality, options.PreferGpuDecodableFormats);
        if (!string.IsNullOrWhiteSpace(format))
        {
            args.Add("-f");
            args.Add(format);
        }

        if (!string.IsNullOrWhiteSpace(options.HttpProxy))
        {
            args.Add("--proxy");
            args.Add(options.HttpProxy.Trim());
        }

        args.Add("--");
        args.Add(url.Trim());
        return args;
    }

    public static string BuildArguments(string url, StreamResolutionOptions options)
    {
        IReadOnlyList<string> args = BuildArgumentList(url, options);
        var sb = new StringBuilder();
        for (int i = 0; i < args.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(' ');
            }

            string arg = args[i];
            bool quote = !arg.StartsWith('-') && !int.TryParse(arg, NumberStyles.None, CultureInfo.InvariantCulture, out _);
            if (quote)
            {
                sb.Append('"').Append(arg.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
            }
            else
            {
                sb.Append(arg);
            }
        }
        return sb.ToString();
    }

    private static int SocketTimeoutSeconds(TimeSpan? timeout)
    {
        if (timeout is not { } value || value <= TimeSpan.Zero)
        {
            return 15;
        }

        int ceiling = Math.Max(1, (int)Math.Ceiling(value.TotalSeconds));
        int clamped = Math.Clamp((int)Math.Ceiling(value.TotalSeconds / 2.0), 5, 30);
        return Math.Min(clamped, ceiling);
    }
}
