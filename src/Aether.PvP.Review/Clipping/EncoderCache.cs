using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Aether.PvP.Review.Clipping;

public interface IEncoderCache
{
    IReadOnlyList<VideoEncoder>? TryRead(string fingerprint);
    void Write(string fingerprint, IReadOnlyList<VideoEncoder> encoders);
    void Invalidate();
}

public sealed class FileEncoderCache : IEncoderCache
{
    private const int SchemaVersion = 1;
    private readonly string _path;
    private readonly object _gate = new();

    public FileEncoderCache(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path) ? DefaultPath() : path;
    }

    public string Path => _path;

    public static string DefaultPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aether",
            "encoder-cache.json");

    public IReadOnlyList<VideoEncoder>? TryRead(string fingerprint)
    {
        if (string.IsNullOrEmpty(fingerprint))
        {
            return null;
        }

        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }

                byte[] bytes = File.ReadAllBytes(_path);
                using var doc = JsonDocument.Parse(bytes);
                JsonElement root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                if (!root.TryGetProperty("version", out JsonElement versionEl)
                    || versionEl.ValueKind != JsonValueKind.Number
                    || !versionEl.TryGetInt32(out int version)
                    || version != SchemaVersion)
                {
                    return null;
                }

                if (!root.TryGetProperty("fingerprint", out JsonElement fpEl)
                    || fpEl.ValueKind != JsonValueKind.String
                    || !string.Equals(fpEl.GetString(), fingerprint, StringComparison.Ordinal))
                {
                    return null;
                }

                if (!root.TryGetProperty("encoders", out JsonElement encodersEl)
                    || encodersEl.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                var result = new List<VideoEncoder>();
                var seen = new HashSet<VideoEncoder>();
                foreach (JsonElement item in encodersEl.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String
                        || !TryParseEncoder(item.GetString(), out VideoEncoder encoder))
                    {
                        return null;
                    }

                    if (seen.Add(encoder))
                    {
                        result.Add(encoder);
                    }
                }

                return result.Count > 0 ? result : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }
    }

    public void Write(string fingerprint, IReadOnlyList<VideoEncoder> encoders)
    {
        if (string.IsNullOrEmpty(fingerprint) || encoders.Count == 0)
        {
            return;
        }

        lock (_gate)
        {
            string temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                string? dir = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("version", SchemaVersion);
                    writer.WriteString("fingerprint", fingerprint);
                    writer.WriteStartArray("encoders");
                    foreach (VideoEncoder encoder in encoders)
                    {
                        writer.WriteStringValue(EncodeName(encoder));
                    }
                    writer.WriteEndArray();
                    writer.WriteString("probedAtUtc", DateTimeOffset.UtcNow);
                    writer.WriteEndObject();
                }

                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { /* best effort */ }
            }
        }
    }

    public void Invalidate()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static string EncodeName(VideoEncoder encoder) => encoder switch
    {
        VideoEncoder.Nvenc => "h264_nvenc",
        VideoEncoder.Qsv => "h264_qsv",
        VideoEncoder.Amf => "h264_amf",
        VideoEncoder.X264 => "libx264",
        _ => throw new ArgumentOutOfRangeException(nameof(encoder), encoder, null)
    };

    private static bool TryParseEncoder(string? value, out VideoEncoder encoder)
    {
        switch (value)
        {
            case "h264_nvenc":
            case "Nvenc":
                encoder = VideoEncoder.Nvenc;
                return true;
            case "h264_qsv":
            case "Qsv":
                encoder = VideoEncoder.Qsv;
                return true;
            case "h264_amf":
            case "Amf":
                encoder = VideoEncoder.Amf;
                return true;
            case "libx264":
            case "X264":
                encoder = VideoEncoder.X264;
                return true;
            default:
                encoder = default;
                return false;
        }
    }
}

internal static partial class HardwareFingerprint
{
    private const string DisplayAdapterClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
    private const nint HkeyLocalMachine = unchecked((nint)(int)0x80000002);
    private const uint KeyQueryValue = 0x0001;
    private const uint KeyEnumerateSubKeys = 0x0008;
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorNoMoreItems = 259;
    private const uint RegSz = 1;
    private const uint RegExpandSz = 2;

    [LibraryImport("advapi32.dll", EntryPoint = "RegOpenKeyExW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int RegOpenKeyExW(
        nint hKey,
        string lpSubKey,
        uint ulOptions,
        uint samDesired,
        out nint phkResult);

    [LibraryImport("advapi32.dll", EntryPoint = "RegEnumKeyExW")]
    private static unsafe partial int RegEnumKeyExW(
        nint hKey,
        uint dwIndex,
        char* lpName,
        ref uint lpcchName,
        nint lpReserved,
        nint lpClass,
        nint lpcchClass,
        nint lpftLastWriteTime);

    [LibraryImport("advapi32.dll", EntryPoint = "RegQueryValueExW", StringMarshalling = StringMarshalling.Utf16)]
    private static unsafe partial int RegQueryValueExW(
        nint hKey,
        string lpValueName,
        nint lpReserved,
        out uint lpType,
        byte* lpData,
        ref uint lpcbData);

    [LibraryImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    private static partial int RegCloseKey(nint hKey);

    public static string Compute(string ffmpegPath)
    {
        var sb = new StringBuilder();

        if (OperatingSystem.IsWindows())
        {
            List<string> gpus = ReadWindowsGpuDrivers();
            foreach (string entry in gpus)
            {
                sb.Append("gpu=").Append(entry).Append('\n');
            }
        }

        string? resolved = ResolveFfmpegPath(ffmpegPath);
        if (resolved is not null)
        {
            string finalPath = Path.GetFullPath(resolved);
            try
            {
                if (File.ResolveLinkTarget(resolved, returnFinalTarget: true) is { } target)
                {
                    finalPath = target.FullName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }

            var info = new FileInfo(finalPath);
            if (info.Exists)
            {
                sb.Append("ffmpeg=").Append(finalPath)
                  .Append('|').Append(info.Length.ToString(CultureInfo.InvariantCulture))
                  .Append('|').Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("ffmpeg=").Append(finalPath).Append("|missing");
            }
        }
        else
        {
            sb.Append("ffmpeg=unresolved:").Append(ffmpegPath.Trim());
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    private static unsafe List<string> ReadWindowsGpuDrivers()
    {
        var entries = new List<string>();
        if (RegOpenKeyExW(HkeyLocalMachine, DisplayAdapterClassKey, 0, KeyEnumerateSubKeys, out nint classKey) != ErrorSuccess)
        {
            return entries;
        }

        try
        {
            const int maxSubkeyNameChars = 256;
            char* nameBuf = stackalloc char[maxSubkeyNameChars];
            for (uint index = 0; ; index++)
            {
                uint nameLen = maxSubkeyNameChars;
                int status = RegEnumKeyExW(classKey, index, nameBuf, ref nameLen, 0, 0, 0, 0);
                if (status == ErrorNoMoreItems)
                {
                    break;
                }
                if (status == ErrorMoreData)
                {
                    continue;
                }
                if (status != ErrorSuccess)
                {
                    break;
                }

                var subkeySpan = new ReadOnlySpan<char>(nameBuf, (int)nameLen);
                if (!IsFourDigitSubkey(subkeySpan))
                {
                    continue;
                }

                string subkeyName = subkeySpan.ToString();
                if (RegOpenKeyExW(classKey, subkeyName, 0, KeyQueryValue, out nint subKey) != ErrorSuccess)
                {
                    continue;
                }

                try
                {
                    string? deviceId = ReadRegistryString(subKey, "MatchingDeviceId");
                    string? version = ReadRegistryString(subKey, "DriverVersion");
                    string? date = ReadRegistryString(subKey, "DriverDate");
                    string? desc = ReadRegistryString(subKey, "DriverDesc");

                    if (deviceId is null && version is null && date is null && desc is null)
                    {
                        continue;
                    }

                    entries.Add(string.Concat(
                        deviceId ?? string.Empty, "|",
                        version ?? string.Empty, "|",
                        date ?? string.Empty, "|",
                        desc ?? string.Empty));
                }
                finally
                {
                    _ = RegCloseKey(subKey);
                }
            }
        }
        finally
        {
            _ = RegCloseKey(classKey);
        }

        entries.Sort(StringComparer.Ordinal);
        return entries;
    }

    private static bool IsFourDigitSubkey(ReadOnlySpan<char> name) =>
        name.Length == 4
        && char.IsAsciiDigit(name[0])
        && char.IsAsciiDigit(name[1])
        && char.IsAsciiDigit(name[2])
        && char.IsAsciiDigit(name[3]);

    private static unsafe string? ReadRegistryString(nint hKey, string valueName)
    {
        const int initialChars = 256;
        char* stackBuf = stackalloc char[initialChars];
        uint cbData = initialChars * sizeof(char);
        int status = RegQueryValueExW(hKey, valueName, 0, out uint type, (byte*)stackBuf, ref cbData);
        if (status == ErrorSuccess && (type == RegSz || type == RegExpandSz) && cbData >= sizeof(char))
        {
            return ExtractRegistryString(new ReadOnlySpan<char>(stackBuf, (int)(cbData / sizeof(char))));
        }

        if (status == ErrorMoreData && cbData >= sizeof(char) && cbData <= 65536)
        {
            int charCount = (int)((cbData + sizeof(char) - 1) / sizeof(char));
            char[] rented = new char[charCount];
            uint retryBytes = (uint)(charCount * sizeof(char));
            fixed (char* p = rented)
            {
                status = RegQueryValueExW(hKey, valueName, 0, out type, (byte*)p, ref retryBytes);
            }
            if (status == ErrorSuccess && (type == RegSz || type == RegExpandSz) && retryBytes >= sizeof(char))
            {
                return ExtractRegistryString(rented.AsSpan(0, (int)(retryBytes / sizeof(char))));
            }
        }

        return null;
    }

    private static string? ExtractRegistryString(ReadOnlySpan<char> raw)
    {
        ReadOnlySpan<char> trimmed = raw.TrimEnd('\0').Trim();
        return trimmed.Length > 0 ? trimmed.ToString() : null;
    }

    private static string? ResolveFfmpegPath(string ffmpegPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegPath))
        {
            return null;
        }

        string trimmed = ffmpegPath.Trim();
        bool hasDirectory = Path.IsPathRooted(trimmed)
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar);

        if (hasDirectory)
        {
            try
            {
                if (TryResolveCandidate(Path.GetFullPath(trimmed)) is { } direct)
                {
                    return direct;
                }
                if (!Path.IsPathRooted(trimmed)
                    && TryResolveCandidate(Path.Combine(AppContext.BaseDirectory, trimmed)) is { } fromBase)
                {
                    return fromBase;
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
            return null;
        }

        if (TryResolveCandidate(Path.Combine(AppContext.BaseDirectory, trimmed)) is { } inBaseDir)
        {
            return inBaseDir;
        }

        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (string rawDir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                string dir = rawDir.Trim().Trim('"');
                if (dir.Length == 0)
                {
                    continue;
                }

                try
                {
                    if (TryResolveCandidate(Path.Combine(dir, trimmed)) is { } onPath)
                    {
                        return onPath;
                    }
                }
                catch (ArgumentException)
                {
                    // Ignore malformed PATH entries.
                }
            }
        }

        return null;
    }

    private static string? TryResolveCandidate(string candidatePath)
    {
        if (File.Exists(candidatePath))
        {
            return candidatePath;
        }

        if (OperatingSystem.IsWindows() && !candidatePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            string withExe = candidatePath + ".exe";
            if (File.Exists(withExe))
            {
                return withExe;
            }
        }

        return null;
    }
}
