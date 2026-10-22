using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OrderCoffee.Services;

/// <summary>
/// Tìm địa chỉ mà điện thoại cùng mạng có thể dùng để vào máy này, để QR không phải ghi cứng IP
/// (đổi Wi-Fi là IP đổi theo).
/// </summary>
public static class LanAddress
{
    private static readonly string[] VirtualMarkers = ["vmware", "virtualbox", "vethernet", "hyper-v", "loopback", "vpn", "tap-", "tunnel", "docker", "wsl", "bluetooth"];

    public record Candidate(string Name, string Ip, bool HasGateway);

    /// <summary>Các IPv4 của card mạng thật đang hoạt động. Card có gateway (đang ra mạng) xếp trước.</summary>
    public static List<Candidate> Candidates()
    {
        var list = new List<Candidate>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is not (NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet)) continue;
            var label = (nic.Name + " " + nic.Description).ToLowerInvariant();
            if (VirtualMarkers.Any(label.Contains)) continue;

            var props = nic.GetIPProperties();
            var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.ToString();
                if (ip.StartsWith("169.254.")) continue;   // chưa xin được IP
                list.Add(new Candidate(nic.Name, ip, hasGateway));
            }
        }
        return list.OrderByDescending(c => c.HasGateway).ToList();
    }

    public static string? CurrentIp() => Candidates().FirstOrDefault()?.Ip;

    /// <summary>Tên máy dạng mDNS (vd. songiang.local): không đổi khi đổi Wi-Fi, nhưng một số điện thoại Android không phân giải được.</summary>
    public static string HostName() => Environment.MachineName.ToLowerInvariant() + ".local";
}
