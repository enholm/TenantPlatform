using System.Net;
using System.Net.Sockets;

namespace TenantPlatform.Web.Security.McpClient;

public static class McpNetworkPolicy
{
    public static void ValidateUrl(string value)
    {
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.Fragment.Length != 0 || uri.Query.Length != 0 || uri.HostNameType == UriHostNameType.Unknown ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && Forbidden(ip)) throw new InvalidOperationException("MCP URL rejected.");
    }
    public static bool Forbidden(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        return IPAddress.IsLoopback(ip) || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.Equals(IPAddress.Parse("fd00:ec2::254")) || ip.Equals(IPAddress.Parse("100.100.100.200")) || ip.Equals(IPAddress.Parse("168.63.129.16")) ||
            ip.AddressFamily == AddressFamily.InterNetwork && (b[0] is 0 or 127 || b[0] >= 224 || b[0] == 169 && b[1] == 254);
    }
    private static bool Private(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        return ip.AddressFamily == AddressFamily.InterNetworkV6 ? (b[0] & 0xe0) != 0x20 :
            b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 100 && b[1] is >= 64 and <= 127 ||
            b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51 && b[2] == 100) ||
            b[0] == 192 && (b[1] == 0 && b[2] is 0 or 2 || b[1] == 88 && b[2] == 99) || b[0] == 203 && b[1] == 0 && b[2] == 113;
    }
    public static bool IsAllowed(IPAddress ip, string[] approved) => !Forbidden(ip) && (!Private(ip) || approved.Any(x => IPAddress.Parse(x).Equals(ip)));
    public static SocketsHttpHandler Handler(string[] approved) => new()
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, ct) =>
        {
            // Resolve once, validate the actual address and connect to that IP: no second DNS/rebinding window.
            var ips = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var allowed = ips.Where(ip => IsAllowed(ip, approved)).ToArray();
            if (allowed.Length == 0) throw new HttpRequestException("MCP destination rejected.");
            var socket = new Socket(allowed[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(new IPEndPoint(allowed[0], context.DnsEndPoint.Port), ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };
}
