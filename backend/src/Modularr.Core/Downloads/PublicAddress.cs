using System.Net;
using System.Net.Sockets;

namespace Modularr.Core.Downloads;

// Whether an address is on the public Internet: anything the server reaches on behalf of remote data (trackers, links) must be,
// never loopback, private, link-local (cloud metadata), carrier-grade NAT, multicast or reserved.
public static class PublicAddress
{
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            byte[] b = address.GetAddressBytes();
            return b[0] switch
            {
                0 or 10 or 127 or >= 224 => false,
                100 => b[1] is < 64 or > 127,
                169 => b[1] != 254,
                172 => b[1] is < 16 or > 31,
                192 => !(b[1] == 168 || (b[1] == 0 && b[2] is 0 or 2) || (b[1] == 88 && b[2] == 99)),
                198 => !(b[1] is 18 or 19 || (b[1] == 51 && b[2] == 100)),
                203 => !(b[1] == 0 && b[2] == 113),
                _ => true,
            };
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            byte[] b = address.GetAddressBytes();
            bool uniqueLocal = (b[0] & 0xFE) == 0xFC;
            bool documentation = b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0D && b[3] == 0xB8;
            return !(address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.IPv6Loopback) || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal || address.IsIPv6Multicast || uniqueLocal || documentation);
        }

        return false;
    }

    // SocketsHttpHandler.ConnectCallback for clients that fetch URLs chosen by remote data: the connection goes to the resolved
    // addresses only if every one is public, so neither a redirect nor a DNS name pointing at the local network gets through.
    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        string host = context.DnsEndPoint.Host;
        IPAddress[] addresses = IPAddress.TryParse(host, out IPAddress? literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || !addresses.All(IsPublic))
        {
            throw new HttpRequestException($"{host} is not a public address; requests to the local network are refused.");
        }

        Socket socket = new(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // A tracker the engine may announce to: udp/http/https to a host that is public by name and by every resolved address.
    public static async Task<bool> IsPublicTrackerAsync(Uri tracker, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tracker);
        if (tracker.Scheme is not ("udp" or "http" or "https") || tracker.UserInfo.Length > 0 || !IsPublicHost(tracker))
        {
            return false;
        }

        if (IPAddress.TryParse(tracker.IdnHost, out _))
        {
            return true;
        }

        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            IPAddress[] addresses = await Dns.GetHostAddressesAsync(tracker.IdnHost, timeout.Token).ConfigureAwait(false);
            return addresses.Length > 0 && addresses.All(IsPublic);
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // Unresolvable now: the engine could not reach it either, and it must not resolve somewhere private later.
            return false;
        }
    }

    // A host name is allowed unless it is a local-only name; an IP literal must be public.
    public static bool IsPublicHost(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (IPAddress.TryParse(uri.IdnHost, out IPAddress? ip))
        {
            return IsPublic(ip);
        }

        string host = uri.IdnHost.TrimEnd('.');
        return uri.HostNameType == UriHostNameType.Dns && host.Contains('.', StringComparison.Ordinal)
            && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            && !host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase);
    }
}
