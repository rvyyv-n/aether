namespace Aether.Core.Playback.Tracks;

public enum TrackKind
{
    Audio,
    Subtitle,
}

/// <summary>One audio or subtitle entry of mpv's <c>track-list</c>.</summary>
/// <param name="Id">mpv's track id: what <c>aid</c> and <c>sid</c> take.</param>
/// <param name="Language">The <c>lang</c> tag (ISO 639 or a culture name), or null when the file has none.</param>
/// <param name="Title">The track title from the file, or null.</param>
/// <param name="ChannelLayout"><c>demux-channels</c> (stereo, 5.1(side)) for audio, or null.</param>
/// <param name="ChannelCount"><c>demux-channel-count</c>, used when the layout name is missing or unfamiliar.</param>
/// <param name="ExternalFile">The path of a loaded subtitle file, or null for a track inside the video file.</param>
public sealed record MediaTrack(
    int Id,
    TrackKind Kind,
    string? Language,
    string? Title,
    string? ChannelLayout,
    int ChannelCount,
    bool HearingImpaired,
    bool Forced,
    bool Selected,
    string? ExternalFile);
