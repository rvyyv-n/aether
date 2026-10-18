using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Aether.Server.LanHost;

public readonly record struct LanCandidate(
    string AdapterName,
    string Description,
    NetworkInterfaceType Type,
    bool IsUp,
    bool HasGateway,
    IPAddress Address);

public static class LanAddressPicker
{
    private static readonly string[] VirtualMarkers =
    {
        "hyper-v", "vethernet", "wsl", "virtualbox", "vmware", "vpn", "tap-", "tun", "wireguard",
        "tailscale", "zerotier", "docker", "loopback", "bluetooth", "pseudo", "npcap", "host-only"
    };

    public static IPAddress? Pick(IEnumerable<LanCandidate> candidates)
    {
        return candidates
            .Where(c => c.IsUp && c.Address.AddressFamily == AddressFamily.InterNetwork)
            .Where(c => c.Type is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet
                                or NetworkInterfaceType.GigabitEthernet)
            .Where(c => !IsVirtual(c) && !IsLoopbackOrLinkLocal(c.Address))
            .OrderByDescending(c => IsPrivate(c.Address))
            .ThenByDescending(c => c.HasGateway)
            .ThenByDescending(c => c.Type == NetworkInterfaceType.Wireless80211)
            .Select(c => c.Address)
            .FirstOrDefault();
    }

    public static IPAddress? PickFromSystem()
    {
        var found = new List<LanCandidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            bool hasGateway = props.GatewayAddresses.Any(g => !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in props.UnicastAddresses)
            {
                found.Add(new LanCandidate(nic.Name, nic.Description, nic.NetworkInterfaceType,
                    nic.OperationalStatus == OperationalStatus.Up, hasGateway, unicast.Address));
            }
        }
        return Pick(found);
    }

    private static bool IsVirtual(LanCandidate c) =>
        VirtualMarkers.Any(m => c.AdapterName.Contains(m, StringComparison.OrdinalIgnoreCase)
                             || c.Description.Contains(m, StringComparison.OrdinalIgnoreCase));

    private static bool IsLoopbackOrLinkLocal(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return IPAddress.IsLoopback(a) || (b[0] == 169 && b[1] == 254) || a.Equals(IPAddress.Any);
    }

    private static bool IsPrivate(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
    }
}
