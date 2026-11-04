using Aether.Core.Playback.Settings;
using Aether.Core.Playback.Streams;

namespace Aether_App;

/// <summary>
/// SponsorBlock (16.1): with the setting on, the segments of the YouTube video now playing are asked for as it opens,
/// and again when the setting or its categories change. UI thread only.
/// </summary>
public sealed partial class MainPage
{
    private static readonly HttpClient SponsorHttp = CreateSponsorHttp();

    private IReadOnlyList<SponsorSegment> _sponsorSegments = [];
    private CancellationTokenSource? _sponsorWork;

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
        _sponsorSegments = segments;
        SeekBar.SkipSegments = segments;
    }
}
