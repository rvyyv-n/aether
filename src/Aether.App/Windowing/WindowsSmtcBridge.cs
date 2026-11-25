using System.Diagnostics;
using System.Runtime.InteropServices;
using Aether.Core.Playback.MediaIntegration;
using Microsoft.UI.Dispatching;
using Windows.Media;

namespace Aether_App.Windowing;

/// <summary>
/// Global Windows System Media Transport Controls (20.1): binds <see cref="SystemMediaTransportControls"/> to the
/// unpackaged window via <c>ISystemMediaTransportControlsInterop::GetForWindow</c> using raw COM vtable calls
/// (ADR-0001 / ADR-0005, Native AOT safe). Media keys work while unfocused, and Windows 11 Quick Settings /
/// lock-screen Now Playing show the live title, source, status and seekable timeline.
/// </summary>
internal sealed partial class WindowsSmtcBridge : IDisposable
{
    private const string RuntimeClassId = "Windows.Media.SystemMediaTransportControls";
    private const int GetForWindowSlot = 6; // IInspectable has 6 slots (0..5)

    private static readonly Guid IidSmtcInterop = new("ddb0472d-c911-4a1f-86d9-dc3d71a95f5a");
    private static readonly Guid IidSmtc = new("99FA3FF4-1742-42A6-902E-087D41F965EC");

    private readonly SystemMediaTransportControls _smtc;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<SmtcCommand> _onCommand;
    private readonly Action<TimeSpan> _onSeek;

    private SmtcMetadata _lastMetadata;
    private SmtcButtonState _lastButtons;
    private SmtcTimeline _lastTimeline;
    private long _lastTimelineTick;
    private bool _hasPublishedTimeline;
    private bool _disposed;

    private WindowsSmtcBridge(
        SystemMediaTransportControls smtc,
        DispatcherQueue dispatcher,
        Action<SmtcCommand> onCommand,
        Action<TimeSpan> onSeek)
    {
        _smtc = smtc;
        _dispatcher = dispatcher;
        _onCommand = onCommand;
        _onSeek = onSeek;

        _smtc.IsEnabled = false;
        _smtc.ButtonPressed += OnButtonPressed;
        _smtc.PlaybackPositionChangeRequested += OnPositionChangeRequested;
    }

    /// <summary>Creates a bridge for <paramref name="hwnd"/>, or returns null if Windows SMTC is unavailable.</summary>
    public static WindowsSmtcBridge? TryCreate(
        nint hwnd,
        DispatcherQueue dispatcher,
        Action<SmtcCommand> onCommand,
        Action<TimeSpan> onSeek)
    {
        if (hwnd == 0)
        {
            return null;
        }
        try
        {
            SystemMediaTransportControls? smtc = GetForWindow(hwnd);
            return smtc is not null ? new WindowsSmtcBridge(smtc, dispatcher, onCommand, onSeek) : null;
        }
        catch (Exception ex) when (ex is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public void Update(in SmtcMetadata metadata, in SmtcButtonState buttons, in SmtcTimeline timeline)
    {
        if (_disposed)
        {
            return;
        }

        if (buttons != _lastButtons)
        {
            _lastButtons = buttons;
            _smtc.IsEnabled = buttons.IsEnabled;
            _smtc.IsPlayEnabled = buttons.IsPlayEnabled;
            _smtc.IsPauseEnabled = buttons.IsPauseEnabled;
            _smtc.IsStopEnabled = buttons.IsStopEnabled;
            _smtc.IsPreviousEnabled = buttons.IsPreviousEnabled;
            _smtc.IsNextEnabled = buttons.IsNextEnabled;
            _smtc.PlaybackStatus = buttons.PlaybackStatus switch
            {
                SmtcPlaybackState.Playing => MediaPlaybackStatus.Playing,
                SmtcPlaybackState.Paused => MediaPlaybackStatus.Paused,
                SmtcPlaybackState.Stopped => MediaPlaybackStatus.Stopped,
                SmtcPlaybackState.Changing => MediaPlaybackStatus.Changing,
                _ => MediaPlaybackStatus.Closed,
            };
        }

        if (metadata != _lastMetadata)
        {
            _lastMetadata = metadata;
            SystemMediaTransportControlsDisplayUpdater updater = _smtc.DisplayUpdater;
            if (!metadata.HasMedia)
            {
                updater.ClearAll();
            }
            else
            {
                updater.Type = MediaPlaybackType.Video;
                updater.VideoProperties.Title = metadata.Title;
                updater.VideoProperties.Subtitle = metadata.Subtitle;
            }
            updater.Update();
        }

        UpdateTimeline(timeline);
    }

    public void UpdateTimeline(in SmtcTimeline timeline)
    {
        if (_disposed || !_lastButtons.IsEnabled)
        {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long elapsedMs = _hasPublishedTimeline
            ? (long)Stopwatch.GetElapsedTime(_lastTimelineTick, now).TotalMilliseconds
            : -1;

        if (!SmtcTimeline.ShouldPush(_lastTimeline, timeline, elapsedMs))
        {
            return;
        }

        _lastTimeline = timeline;
        _lastTimelineTick = now;
        _hasPublishedTimeline = true;

        var props = new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = timeline.StartTime,
            EndTime = timeline.EndTime,
            Position = timeline.Position,
            MinSeekTime = timeline.MinSeekTime,
            MaxSeekTime = timeline.MaxSeekTime,
        };
        _smtc.UpdateTimelineProperties(props);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            _smtc.ButtonPressed -= OnButtonPressed;
            _smtc.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
            _smtc.IsEnabled = false;
            _smtc.DisplayUpdater.ClearAll();
            _smtc.DisplayUpdater.Update();
        }
        catch (COMException)
        {
            // The window or shell media service may already be tearing down.
        }
    }

    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        SmtcCommand? command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => SmtcCommand.Play,
            SystemMediaTransportControlsButton.Pause => SmtcCommand.Pause,
            SystemMediaTransportControlsButton.Stop => SmtcCommand.Stop,
            SystemMediaTransportControlsButton.Previous => SmtcCommand.Previous,
            SystemMediaTransportControlsButton.Next => SmtcCommand.Next,
            _ => null,
        };
        if (command is { } cmd)
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (!_disposed)
                {
                    _onCommand(cmd);
                }
            });
        }
    }

    private void OnPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        TimeSpan target = args.RequestedPlaybackPosition;
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed)
            {
                _onSeek(target);
            }
        });
    }

    private static unsafe SystemMediaTransportControls? GetForWindow(nint hwnd)
    {
        nint hstring = 0;
        nint interopFactory = 0;
        nint smtcAbi = 0;
        try
        {
            int hr = WindowsCreateString(RuntimeClassId, (uint)RuntimeClassId.Length, out hstring);
            if (hr < 0)
            {
                return null;
            }

            Guid interopIid = IidSmtcInterop;
            hr = RoGetActivationFactory(hstring, in interopIid, out interopFactory);
            if (hr < 0 || interopFactory == 0)
            {
                return null;
            }

            Guid smtcIid = IidSmtc;
            var getForWindow = (delegate* unmanaged[Stdcall]<nint, nint, Guid*, nint*, int>)(*(void***)interopFactory)[GetForWindowSlot];
            hr = getForWindow(interopFactory, hwnd, &smtcIid, &smtcAbi);
            if (hr < 0 || smtcAbi == 0)
            {
                return null;
            }

            return SystemMediaTransportControls.FromAbi(smtcAbi);
        }
        finally
        {
            ReleaseCom(ref smtcAbi);
            ReleaseCom(ref interopFactory);
            if (hstring != 0)
            {
                _ = WindowsDeleteString(hstring);
            }
        }
    }

    private static unsafe void ReleaseCom(ref nint ptr)
    {
        nint value = Interlocked.Exchange(ref ptr, 0);
        if (value != 0)
        {
            _ = ((delegate* unmanaged[Stdcall]<nint, uint>)(*(void***)value)[2])(value);
        }
    }

    [LibraryImport("api-ms-win-core-winrt-l1-1-0.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, in Guid iid, out nint factory);

    [LibraryImport("api-ms-win-core-winrt-string-l1-1-0.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string sourceString, uint length, out nint hstring);

    [LibraryImport("api-ms-win-core-winrt-string-l1-1-0.dll")]
    private static partial int WindowsDeleteString(nint hstring);
}
