using Aether.Core.Playback;
using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;

namespace Aether_App;

/// <summary>
/// SponsorBlock (16.1, 16.3): with the setting on, the segments of the YouTube video now playing are asked for as it
/// opens, and again when the setting or its categories change. Playing into one skips it with a toast to go back, or with
/// Skip automatically off, offers the Skip button the chapters use. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private static readonly HttpClient SponsorHttp = CreateSponsorHttp();

    private readonly SegmentSkipper _segmentSkipper = new();
    private CancellationTokenSource? _sponsorWork;

    /// <summary>The segment playing now when Skip automatically is off, for the Skip button; null otherwise.</summary>
    private SegmentSkip? _sponsorOffer;

    private static HttpClient CreateSponsorHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Aether");
        return http;
    }

    private void InitializeSponsorBlock()
    {
        Loaded += (_, _) => App.Settings.Changed += OnSponsorSettingChanged;
        Unloaded += (_, _) => App.Settings.Changed -= OnSponsorSettingChanged;
    }

    private void OnSponsorSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Key is SettingKeys.SponsorBlock or SettingKeys.SponsorBlockCategories)
        {
            DispatcherQueue.TryEnqueue(() => LoadSponsorSegments(_currentWebLink));
        }
    }

    private async void LoadSponsorSegments(string? webLink)
    {
        _sponsorWork?.Cancel();
        ShowSponsorSegments([]);
        AetherSettings settings = App.Settings.Current;
        if (!settings.SponsorBlock || SponsorBlock.VideoId(webLink) is not { } id)
        {
            return;
        }
        var work = new CancellationTokenSource();
        _sponsorWork = work;
        IReadOnlyList<SponsorSegment> segments;
        try
        {
            segments = await SponsorBlock.FetchAsync(SponsorHttp, id, SponsorBlock.ParseCategories(settings.SponsorBlockCategories), work.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!work.IsCancellationRequested && _currentWebLink == webLink)
        {
            ShowSponsorSegments(segments);
        }
    }

    private void ShowSponsorSegments(IReadOnlyList<SponsorSegment> segments)
    {
        _segmentSkipper.Reset(segments);
        _sponsorOffer = null;
        SeekBar.SkipSegments = segments;
    }

    private void OnSponsorPosition(PlayerViewModel vm)
    {
        if (_session is not { } session || !HasVideo)
        {
            return;
        }
        if (!App.Settings.Current.SponsorBlockAutoSkip)
        {
            _sponsorOffer = _segmentSkipper.Offer(vm.Position);
            return;
        }
        _sponsorOffer = null;
        if (_segmentSkipper.OnPosition(vm.Position) is not { } skip)
        {
            return;
        }
        TrySend(() => session.Seek(skip.Target, dragging: false));
        _toasts?.Show("sponsor-skipped", AppStrings.Format("Toast_ChapterSkipped", CategoryName(skip.Segment.Category)), ToastTone.Neutral,
            AppStrings.Get("Toast_ChapterSkipBack"), () =>
            {
                _segmentSkipper.OnUserSeek(skip.Segment.Start);
                TrySend(() => session.Seek(skip.Segment.Start, dragging: false));
            });
    }

    /// <summary>The Skip button's offer while a segment plays, as a chapter offer so the one button serves both.</summary>
    private ChapterSkipOffer? SponsorSkipOffer =>
        _sponsorOffer is { } skip ? new ChapterSkipOffer(skip.Target, CategoryName(skip.Segment.Category)) : null;

    private void UseSponsorOffer()
    {
        if (_sponsorOffer is { } skip)
        {
            _segmentSkipper.Done(skip);
            _sponsorOffer = null;
        }
    }

    private static string CategoryName(SponsorCategory category) => category switch
    {
        SponsorCategory.Sponsor => AppStrings.Get("SponsorBlock_Sponsor"),
        SponsorCategory.SelfPromo => AppStrings.Get("SponsorBlock_SelfPromo"),
        SponsorCategory.Interaction => AppStrings.Get("SponsorBlock_Interaction"),
        SponsorCategory.Intro => AppStrings.Get("SponsorBlock_Intro"),
        _ => AppStrings.Get("SponsorBlock_Outro"),
    };
}
