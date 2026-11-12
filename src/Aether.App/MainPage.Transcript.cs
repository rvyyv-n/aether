using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Aether.Core.Playback.Tracks;

namespace Aether_App;

/// <summary>
/// The transcript pane's text (15.3): the subtitle on screen when it is text, else a subtitle file, else an embedded
/// text track, which ffmpeg writes out as SRT when it is on the PATH. It follows a change of subtitle or file and the
/// playing position. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    // What the pane holds: a subtitle file's path, or the video and the stream index of an embedded track; null to reload.
    private string? _transcriptKey;
    private CancellationTokenSource? _transcriptWork;

    /// <summary>Reads the tracks afresh; <see cref="FollowTranscript"/> runs after every read of them.</summary>
    private void LoadTranscript()
    {
        _transcriptKey = null;
        TranscriptPane.Load([], "", 0, AppStrings.Get(HasVideo ? "Transcript_Reading" : "Transcript_NoVideo"));
        if (HasVideo)
        {
            _ = ChangeTracks(_ => { });
        }
    }

    /// <summary>Once a new file has loaded, the open pane reads it.</summary>
    private void LoadTranscriptOnceLoaded()
    {
        if (_sidePanel == SidePanel.Transcript && HasVideo && _transcriptKey is null)
        {
            _ = ChangeTracks(_ => { });
        }
    }

    private async void FollowTranscript(TrackSnapshot snapshot)
    {
        if (_sidePanel != SidePanel.Transcript || _currentPath is not { } video || !HasVideo)
        {
            return;
        }
        MediaTrack? source = Transcript.Source(snapshot.Tracks);
        string key = source is null ? video : source.ExternalFile ?? $"{video}#{source.StreamIndex}";
        if (key == _transcriptKey)
        {
            return;
        }
        _transcriptKey = key;
        _transcriptWork?.Cancel();
        var work = new CancellationTokenSource();
        _transcriptWork = work;
        double duration = _session?.ViewModel.Duration ?? 0;
        if (source is null)
        {
            TranscriptPane.Load([], "", duration, AppStrings.Get("Transcript_None"));
            return;
        }
        string name = source.ExternalFile is { } file
            ? Path.GetFileName(file)
            : TrackLabels.Format(source, AppStrings.Get("Tracks_TrackN"), AppStrings.Get("Tracks_Sdh"), AppStrings.Get("Tracks_Forced"), _streamTitle);
        TranscriptPane.Load([], name, duration, AppStrings.Get("Transcript_Reading"));
        (IReadOnlyList<TranscriptLine> lines, string? problem) = await Task.Run(() => ReadTranscript(video, source, work.Token));
        if (work.IsCancellationRequested || _transcriptKey != key)
        {
            return;
        }
        TranscriptPane.Load(lines, name, duration, AppStrings.Get(problem ?? "Transcript_Empty"));
        TranscriptPane.SetPosition(_session?.ViewModel.Position ?? 0);
    }

    /// <summary>The lines, or none and the string key of what went wrong.</summary>
    private static (IReadOnlyList<TranscriptLine> Lines, string? Problem) ReadTranscript(string video, MediaTrack source, CancellationToken ct)
    {
        if (source.ExternalFile is { } file)
        {
            try
            {
                return (Transcript.Parse(File.ReadAllText(file), Path.GetExtension(file)), null);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return ([], "Transcript_Unreadable");
            }
        }
        var start = new ProcessStartInfo("ffmpeg")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string argument in Transcript.ExtractArguments(video, source.StreamIndex ?? 0))
        {
            start.ArgumentList.Add(argument);
        }
        try
        {
            using Process ffmpeg = Process.Start(start)!;
            using CancellationTokenRegistration stop = ct.Register(() =>
            {
                try
                {
                    ffmpeg.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                }
            });
            string srt = ffmpeg.StandardOutput.ReadToEnd();
            ffmpeg.WaitForExit();
            return ffmpeg.ExitCode == 0 ? (Transcript.Parse(srt, ".srt"), null) : ([], "Transcript_Unreadable");
        }
        catch (Win32Exception)
        {
            return ([], "Transcript_NeedsFfmpeg");
        }
    }
}
