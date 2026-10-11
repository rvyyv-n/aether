namespace Aether.Core.Playback.Settings;

/// <summary>One selectable choice for the hardware decoding setting.</summary>
public readonly record struct HardwareDecodingOption(string Value, string ResourceKey);

/// <summary>The selectable hardware decoding modes in display order.</summary>
public static class HardwareDecodingOptions
{
    public static readonly IReadOnlyList<HardwareDecodingOption> All =
    [
        new("auto-safe", "Settings_HardwareDecoding_Auto"),
        new("no", "Settings_HardwareDecoding_SoftwareOnly"),
        new("d3d11va", "Settings_HardwareDecoding_Direct3D11"),
        new("d3d11va-copy", "Settings_HardwareDecoding_Direct3D11Copy"),
    ];
}
