using System;
using System.Collections.Generic;
using System.Net;

namespace Aether.Server.LanHost;

/// <summary>
/// Counts the devices watching: an address counts while it has a video request open, and for
/// <see cref="Linger"/> after its last one ends, because phone browsers fetch in bursts with quiet gaps between.
/// Thread-safe; Kestrel calls in from its own threads.
/// </summary>
public sealed class WatcherTracker
{
    public static readonly TimeSpan Linger = TimeSpan.FromSeconds(30);

    private readonly Dictionary<IPAddress, (int Open, long LastMilliseconds)> _seen = new();
    private readonly object _gate = new();

    public void Begin(IPAddress address, long nowMilliseconds)
    {
        address = Normalise(address);
        lock (_gate)
        {
            _seen.TryGetValue(address, out var entry);
            _seen[address] = (entry.Open + 1, nowMilliseconds);
        }
    }

    public void End(IPAddress address, long nowMilliseconds)
    {
        address = Normalise(address);
        lock (_gate)
        {
            if (_seen.TryGetValue(address, out var entry))
            {
                _seen[address] = (Math.Max(0, entry.Open - 1), nowMilliseconds);
            }
        }
    }

    public int Count(long nowMilliseconds)
    {
        long cutoff = nowMilliseconds - (long)Linger.TotalMilliseconds;
        lock (_gate)
        {
            List<IPAddress>? gone = null;
            foreach (var (address, entry) in _seen)
            {
                if (entry.Open == 0 && entry.LastMilliseconds < cutoff)
                {
                    (gone ??= []).Add(address);
                }
            }
            if (gone is not null)
            {
                foreach (IPAddress address in gone)
                {
                    _seen.Remove(address);
                }
            }
            return _seen.Count;
        }
    }

    // Kestrel listening on IPAddress.Any can report IPv4 phones as IPv4-mapped IPv6.
    private static IPAddress Normalise(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
