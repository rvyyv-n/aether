namespace Aether_App.Controls;

/// <summary>The names shown for the encoder that made a Precise clip (stored on the shelf as the enum's name).</summary>
internal static class ClipEncoderNames
{
    public static string? Short(string? encoder) => Key(encoder) is { } key ? AppStrings.Get($"Clips_Enc{key}") : null;

    public static string? Long(string? encoder) =>
        Key(encoder) is { } key ? string.Format(AppStrings.Get("Clips_EncTip"), AppStrings.Get($"Clips_Enc{key}Long")) : null;

    private static string? Key(string? encoder) => encoder switch
    {
        "Nvenc" => "Nvenc",
        "Qsv" => "Qsv",
        "Amf" => "Amf",
        "X264" => "X264",
        _ => null,
    };
}
