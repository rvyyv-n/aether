namespace Aether.Core.Playback;

/// <summary>What the queue does past its last item, and when a file plays to its end.</summary>
public enum RepeatMode
{
    None,
    All,
    One,
}

/// <summary>
/// The play queue (13.1): local files and links in the order the user sees them, which one is playing, and the order
/// they play in, which is the list order or, with <see cref="Shuffle"/> on, a shuffle that plays each item once before
/// any repeats. The same file can be queued twice. Safe to use from any thread.
/// </summary>
public sealed class PlaylistStore(Random? random = null)
{
    // An object per queued item, so a file queued twice is two entries and the playing one is never ambiguous.
    private sealed class Entry(string item)
    {
        public string Item { get; } = item;
    }

    private readonly Lock _lock = new();
    private readonly Random _random = random ?? Random.Shared;
    private List<Entry> _items = [];
    private List<Entry> _order = [];
    private Entry? _current;
    // The playing entry's place in _order; with nothing playing, the place just before the item that plays next.
    private int _cursor = -1;
    private RepeatMode _repeat;
    private bool _shuffle;

    public IReadOnlyList<string> Items
    {
        get
        {
            lock (_lock)
            {
                return _items.Select(e => e.Item).ToArray();
            }
        }
    }

    public string? Current
    {
        get
        {
            lock (_lock)
            {
                return _current?.Item;
            }
        }
    }

    /// <summary>Where the playing item is in <see cref="Items"/>; -1 when nothing in the queue is playing.</summary>
    public int CurrentIndex
    {
        get
        {
            lock (_lock)
            {
                return _current is null ? -1 : _items.IndexOf(_current);
            }
        }
    }

    public RepeatMode Repeat
    {
        get
        {
            lock (_lock)
            {
                return _repeat;
            }
        }
        set
        {
            lock (_lock)
            {
                _repeat = value;
            }
        }
    }

    /// <summary>On: a new shuffled order that starts from the playing item. Off: the list order again, from the playing item.</summary>
    public bool Shuffle
    {
        get
        {
            lock (_lock)
            {
                return _shuffle;
            }
        }
        set
        {
            lock (_lock)
            {
                if (value == _shuffle)
                {
                    return;
                }
                _shuffle = value;
                if (value)
                {
                    Reshuffle(first: _current, avoidFirst: null);
                }
                else
                {
                    _order = [.. _items];
                }
                _cursor = _current is null ? -1 : _order.IndexOf(_current);
            }
        }
    }

    public bool CanGoNext
    {
        get
        {
            lock (_lock)
            {
                return _order.Count > 0 && (_cursor + 1 < _order.Count || _repeat != RepeatMode.None);
            }
        }
    }

    public bool CanGoPrevious
    {
        get
        {
            lock (_lock)
            {
                return _order.Count > 0 && (PreviousPosition() >= 0 || _repeat != RepeatMode.None);
            }
        }
    }

    /// <summary>
    /// Replaces the queue with <paramref name="items"/> and makes <paramref name="current"/> (matched ignoring case) the
    /// playing item. A listing the same as the queue's, as when a folder is read again, keeps the play order.
    /// </summary>
    public void Load(IEnumerable<string> items, string? current)
    {
        ArgumentNullException.ThrowIfNull(items);
        string[] listing = items.ToArray();
        lock (_lock)
        {
            bool same = listing.Length == _items.Count
                && listing.Zip(_items).All(pair => string.Equals(pair.First, pair.Second.Item, StringComparison.OrdinalIgnoreCase));
            if (!same)
            {
                _items = listing.Select(item => new Entry(item)).ToList();
            }
            _current = _current is not null && same && string.Equals(_current.Item, current, StringComparison.OrdinalIgnoreCase)
                ? _current
                : _items.Find(e => string.Equals(e.Item, current, StringComparison.OrdinalIgnoreCase));
            if (!same)
            {
                if (_shuffle)
                {
                    Reshuffle(first: _current, avoidFirst: null);
                }
                else
                {
                    _order = [.. _items];
                }
            }
            _cursor = _current is null ? -1 : _order.IndexOf(_current);
        }
    }

    /// <summary>Adds to the end of the list; with shuffle on, each lands somewhere in what is still to play.</summary>
    public void Enqueue(IEnumerable<string> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        lock (_lock)
        {
            foreach (string item in items)
            {
                var entry = new Entry(item);
                _items.Add(entry);
                _order.Insert(_shuffle ? _random.Next(_cursor + 1, _order.Count + 1) : _order.Count, entry);
            }
        }
    }

    /// <summary>Removes the item at <paramref name="index"/> in <see cref="Items"/>. Removing the playing one leaves nothing playing, and next goes to what followed it.</summary>
    public void RemoveAt(int index)
    {
        lock (_lock)
        {
            Entry entry = _items[index];
            _items.RemoveAt(index);
            int position = _order.IndexOf(entry);
            _order.RemoveAt(position);
            if (entry == _current)
            {
                _current = null;
                _cursor = position - 1;
            }
            else if (position < _cursor || (_current is null && position == _cursor))
            {
                _cursor--;
            }
        }
    }

    /// <summary>Moves an item in <see cref="Items"/>; without shuffle the play order follows.</summary>
    public void Move(int from, int to)
    {
        lock (_lock)
        {
            Entry entry = _items[from];
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(to, _items.Count);
            _items.RemoveAt(from);
            _items.Insert(to, entry);
            if (!_shuffle)
            {
                _order = [.. _items];
                if (_current is not null)
                {
                    _cursor = _order.IndexOf(_current);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _items.Clear();
            _order.Clear();
            _current = null;
            _cursor = -1;
        }
    }

    /// <summary>Makes the item at <paramref name="index"/> in <see cref="Items"/> the playing one and returns it.</summary>
    public string Select(int index)
    {
        lock (_lock)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _items.Count);
            _current = _items[index];
            _cursor = _order.IndexOf(_current);
            return _current.Item;
        }
    }

    /// <summary>
    /// Moves on and returns the item to play, or null at the end of the queue with repeat off. <paramref name="ended"/>
    /// is true when the playing file reached its end, where Repeat One plays it again; a skip always moves on.
    /// </summary>
    public string? Next(bool ended)
    {
        lock (_lock)
        {
            if (ended && _repeat == RepeatMode.One && _current is not null)
            {
                return _current.Item;
            }
            int target = _cursor + 1;
            if (target >= _order.Count)
            {
                if (_repeat == RepeatMode.None || _order.Count == 0)
                {
                    return null;
                }
                if (_shuffle)
                {
                    Reshuffle(first: null, avoidFirst: _current);
                }
                target = 0;
            }
            return MoveTo(target);
        }
    }

    /// <summary>Goes back one in the play order and returns that item, or null at the start with repeat off.</summary>
    public string? Previous()
    {
        lock (_lock)
        {
            int target = PreviousPosition();
            if (target < 0)
            {
                if (_repeat == RepeatMode.None || _order.Count == 0)
                {
                    return null;
                }
                target = _order.Count - 1;
            }
            return MoveTo(target);
        }
    }

    private int PreviousPosition() => _current is null ? _cursor : _cursor - 1;

    private string MoveTo(int position)
    {
        _cursor = position;
        _current = _order[position];
        return _current.Item;
    }

    // Fisher-Yates over the list. A new round after the last item must not start with the item that just played.
    private void Reshuffle(Entry? first, Entry? avoidFirst)
    {
        _order = [.. _items];
        for (int i = _order.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        if (first is not null)
        {
            int at = _order.IndexOf(first);
            (_order[0], _order[at]) = (_order[at], _order[0]);
        }
        else if (avoidFirst is not null && _order.Count > 1 && _order[0] == avoidFirst)
        {
            int swap = _random.Next(1, _order.Count);
            (_order[0], _order[swap]) = (_order[swap], _order[0]);
        }
    }
}
