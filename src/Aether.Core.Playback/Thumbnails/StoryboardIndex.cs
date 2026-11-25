using Aether.Core.Playback.Streams;

namespace Aether.Core.Playback.Thumbnails;

/// <summary>One crop rectangle inside a remote storyboard sprite sheet.</summary>
public sealed record StoryboardTile(
    int FragmentIndex,
    string SheetUrl,
    int CropX,
    int CropY,
    int Width,
    int Height);

/// <summary>Selects a storyboard tier and maps playback timestamps to sprite sheet tiles.</summary>
public static class StoryboardIndex
{
    public static StreamStoryboard? SelectBest(IReadOnlyList<StreamStoryboard>? storyboards, int preferredWidth = 160)
    {
        if (storyboards is null || storyboards.Count == 0)
        {
            return null;
        }

        StreamStoryboard? preferredOrLarger = null;
        StreamStoryboard? largest = null;

        foreach (StreamStoryboard sb in storyboards)
        {
            if (!IsUsable(sb))
            {
                continue;
            }

            if (sb.TileWidth >= preferredWidth)
            {
                if (preferredOrLarger is null
                    || sb.TileWidth < preferredOrLarger.TileWidth
                    || (sb.TileWidth == preferredOrLarger.TileWidth && sb.TileHeight > preferredOrLarger.TileHeight))
                {
                    preferredOrLarger = sb;
                }
            }

            long area = (long)sb.TileWidth * sb.TileHeight;
            if (largest is null || area > (long)largest.TileWidth * largest.TileHeight)
            {
                largest = sb;
            }
        }

        return preferredOrLarger ?? largest;
    }

    public static StoryboardTile? Locate(StreamStoryboard? storyboard, double seconds)
    {
        if (!IsUsable(storyboard) || !double.IsFinite(seconds))
        {
            return null;
        }

        StreamStoryboard sb = storyboard!;
        double clamped = Math.Max(0, seconds);
        double elapsed = 0;
        int fragmentIndex = sb.Fragments.Count - 1;
        double offsetInFragment = Math.Max(0, sb.Fragments[^1].Duration - 0.001);

        for (int i = 0; i < sb.Fragments.Count; i++)
        {
            double duration = sb.Fragments[i].Duration;
            if (duration <= 0 || !double.IsFinite(duration))
            {
                continue;
            }

            if (clamped < elapsed + duration || i == sb.Fragments.Count - 1)
            {
                fragmentIndex = i;
                offsetInFragment = Math.Clamp(clamped - elapsed, 0, Math.Max(0, duration - 0.001));
                break;
            }

            elapsed += duration;
        }

        int tilesPerSheet = sb.Rows * sb.Columns;
        double firstDuration = sb.Fragments[0].Duration;
        double interval = sb.Fps is > 0 && double.IsFinite(sb.Fps.Value)
            ? 1.0 / sb.Fps.Value
            : (firstDuration > 0 && double.IsFinite(firstDuration) ? firstDuration / tilesPerSheet : 1.0);
        if (interval <= 0 || !double.IsFinite(interval))
        {
            interval = 1.0;
        }

        int tileIndex = Math.Clamp((int)Math.Floor(offsetInFragment / interval), 0, tilesPerSheet - 1);
        int row = tileIndex / sb.Columns;
        int column = tileIndex % sb.Columns;

        return new StoryboardTile(
            FragmentIndex: fragmentIndex,
            SheetUrl: sb.Fragments[fragmentIndex].Url,
            CropX: column * sb.TileWidth,
            CropY: row * sb.TileHeight,
            Width: sb.TileWidth,
            Height: sb.TileHeight);
    }

    private static bool IsUsable(StreamStoryboard? storyboard) =>
        storyboard is not null
        && storyboard.TileWidth > 0
        && storyboard.TileHeight > 0
        && storyboard.Rows > 0
        && storyboard.Columns > 0
        && storyboard.Fragments.Count > 0;
}
