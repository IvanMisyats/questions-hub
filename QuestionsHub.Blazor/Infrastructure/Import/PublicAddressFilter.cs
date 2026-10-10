using System.Net;
using System.Net.Sockets;

namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Keeps server-side downloads of user-supplied URLs (asset URLs in an imported .qhub) on the public
/// internet. The check runs in the connection callback against the addresses actually connected to,
/// for every connection including redirects, so neither a redirect nor a public name resolving to an
/// internal address can reach loopback, the Docker network or other private hosts.
/// </summary>
public static class PublicAddressFilter
{
    /// <summary>A <see cref="SocketsHttpHandler"/> whose connections are limited to public addresses.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        ConnectCallback = Connect,
        UseProxy = false,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    };

    private static async ValueTask<Stream> Connect(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.Length == 0 || !addresses.All(IsPublic))
            throw new HttpRequestException($"Адреса {host} недоступна для завантаження");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// True for globally routable unicast addresses; false for loopback, private, link-local, shared
    /// (CGNAT), multicast, reserved and documentation ranges, and IPv6 forms that embed IPv4.
    /// </summary>
    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0                                      // "this" network
                || b[0] == 10                                       // private
                || b[0] == 127                                      // loopback
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)       // shared address space (CGNAT)
                || (b[0] == 169 && b[1] == 254)                     // link-local, cloud metadata
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)        // private
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // IETF protocol assignments
                || (b[0] == 192 && b[1] == 0 && b[2] == 2)          // documentation
                || (b[0] == 192 && b[1] == 168)                     // private
                || (b[0] == 198 && (b[1] == 18 || b[1] == 19))      // benchmarking
                || (b[0] == 198 && b[1] == 51 && b[2] == 100)       // documentation
                || (b[0] == 203 && b[1] == 0 && b[2] == 113)        // documentation
                || b[0] >= 224);                                    // multicast, reserved, broadcast
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            var globalUnicast = (b[0] & 0xE0) == 0x20;              // 2000::/3
            return globalUnicast
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8)  // documentation
                && !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00)  // Teredo (embeds IPv4)
                && !(b[0] == 0x20 && b[1] == 0x02);                                 // 6to4 (embeds IPv4)
        }

        return false;
    }
}
