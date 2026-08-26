using System.Net;

namespace CondotifyAPI.Services.Security;

internal static class EquipmentNetworkSecurity
{
    internal static bool IsAllowedAddress(string? value)
    {
        if (!IPAddress.TryParse(value, out var address)) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 0
                && bytes[0] < 224
                && !(bytes[0] == 169 && bytes[1] == 254);
        }

        return !address.Equals(IPAddress.IPv6Any)
            && !address.Equals(IPAddress.IPv6None)
            && !address.IsIPv6LinkLocal
            && !address.IsIPv6Multicast;
    }
}
