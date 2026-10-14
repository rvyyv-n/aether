using System.Text;
using Aether.Core.Playback.Settings;

namespace Aether.Core.Playback.Streams;

public static class YtDlpArgumentsBuilder
{
    public static string BuildArguments(string url, StreamResolutionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(options);

        var sb = new StringBuilder();
        sb.Append("--dump-json --no-playlist ");

        string format = MpvSettingsMap.YtdlFormat(options.StreamingQuality, options.PreferGpuDecodableFormats);
        if (!string.IsNullOrWhiteSpace(format))
        {
            sb.Append("-f ").Append((char)34).Append(format).Append((char)34).Append(' ');
        }

        if (!string.IsNullOrWhiteSpace(options.HttpProxy))
        {
            sb.Append("--proxy ").Append((char)34).Append(options.HttpProxy).Append((char)34).Append(' ');
        }

        sb.Append((char)34).Append(url.Trim()).Append((char)34);
        return sb.ToString();
    }
}
