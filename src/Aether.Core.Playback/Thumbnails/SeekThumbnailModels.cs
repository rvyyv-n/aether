namespace Aether.Core.Playback.Thumbnails;

/// <summary>Decoded preview bitmap bytes in tightly packed BGRA8 format (<c>Width * Height * 4</c>).</summary>
public sealed record ThumbnailFrame(int Width, int Height, byte[] BgraPixels);

/// <summary>Adaptive seek-bar preview bucket math and aspect-preserving thumbnail sizing.</summary>
public static class ThumbnailTiming
{
    public const int DefaultMaxWidth = 192;
    public const int DefaultMaxHeight = 120;

    /// <summary>Chooses the step between cached preview positions from the media duration.</summary>
    public static double ComputeBucketSeconds(double duration)
    {
        if (!double.IsFinite(duration) || duration <= 0)
        {
            return 1.0;
        }

        if (duration <= 120)
        {
            return 1.0;
        }
        if (duration <= 600)
        {
            return 2.0;
        }
        if (duration <= 3600)
        {
            return 5.0;
        }
        return Math.Max(5.0, Math.Ceiling(duration / 360.0));
    }

    /// <summary>Maps a hover timestamp onto a stable bucket index and decode target time.</summary>
    public static (int BucketIndex, double TargetSeconds) QuantizeBucket(double seconds, double duration)
    {
        if (!double.IsFinite(seconds) || !double.IsFinite(duration) || duration <= 0)
        {
            return (0, 0);
        }

        double interval = ComputeBucketSeconds(duration);
        double clamped = Math.Clamp(seconds, 0, Math.Max(0, duration - 0.01));
        int bucket = Math.Max(0, (int)Math.Floor(clamped / interval));
        double target = Math.Min(bucket * interval, Math.Max(0, duration - 0.05));
        return (bucket, Math.Max(0, target));
    }

    /// <summary>Fits the source video dimensions inside the hover bubble with even pixel dimensions.</summary>
    public static (int Width, int Height) FitThumbnailSize(
        int sourceWidth,
        int sourceHeight,
        int maxWidth = DefaultMaxWidth,
        int maxHeight = DefaultMaxHeight)
    {
        int cappedMaxWidth = Math.Max(64, maxWidth & ~1);
        int cappedMaxHeight = Math.Max(36, maxHeight & ~1);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return (Math.Min(192, cappedMaxWidth), Math.Min(108, cappedMaxHeight));
        }

        double scale = Math.Min((double)cappedMaxWidth / sourceWidth, (double)cappedMaxHeight / sourceHeight);
        int width = Math.Clamp(((int)Math.Round(sourceWidth * scale)) & ~1, 64, cappedMaxWidth);
        int height = Math.Clamp(((int)Math.Round(sourceHeight * scale)) & ~1, 36, cappedMaxHeight);
        return (width, height);
    }
}

/// <summary>Thread-safe bounded LRU cache used for decoded preview frames and storyboard sprite sheets.</summary>
public sealed class ThumbnailLruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Lock _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _map = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _lru = [];

    public ThumbnailLruCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _map.Count;
            }
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                value = node.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }
    }

    public void Set(TKey key, TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<(TKey Key, TValue Value)>? existing))
            {
                existing.Value = (key, value);
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return;
            }

            if (_map.Count >= _capacity && _lru.Last is { } oldest)
            {
                _lru.RemoveLast();
                _map.Remove(oldest.Value.Key);
            }

            var node = new LinkedListNode<(TKey Key, TValue Value)>((key, value));
            _lru.AddFirst(node);
            _map[key] = node;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _map.Clear();
            _lru.Clear();
        }
    }
}
