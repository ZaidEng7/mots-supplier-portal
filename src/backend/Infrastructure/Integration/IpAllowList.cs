// Whether a caller's address is one a key is allowed to be used from.
//
// AN EMPTY LIST ALLOWS EVERYTHING, and that is the important default rather than a lazy one. The ministry's
// requirements ask for an allow-list on the server that runs their nightly load, and nobody knows that server's
// address yet. A key with no list is therefore the normal case today, and it must keep working - a list that
// defaulted to "nothing" would refuse every key the moment this shipped.
//
// THE COMPARISON IS ON BYTES, NOT ON TEXT. An address written 10.42.0.9 and one written 010.042.000.009 are the
// same address and look nothing alike; ::ffff:10.42.0.9 is that address again in another family. Parsing both
// sides and comparing the bytes is the only way those agree, and string matching on a prefix - which is the
// obvious shortcut - would let 10.42.0.99 pass a list holding 10.42.0.9.
//
// A DUAL-STACK SERVER REPORTS AN OLD-STYLE CALLER AS A NEW-STYLE ADDRESS wrapping it, which is why a mapped
// address is unwrapped before anything is compared. Without that, a list written in the form everybody writes
// would match nothing at all on exactly the deployments most likely to be used.
//
// AN ENTRY THAT DOES NOT PARSE MATCHES NOTHING rather than matching everything. The validator refuses nonsense
// at the point a key is created, so this is the second line: if a bad entry ever reached the database, the
// failure it causes must be a refused request rather than an open door.
//
// THE PREFIX LENGTH IS BOUNDED BY THE FAMILY. /33 is not a thing for an old-style address, and treating it as
// one would mask more bits than exist and match by accident.

namespace MotsSupplierPortal.Infrastructure.Integration;

using System.Net;
using System.Net.Sockets;

public static class IpAllowList
{
    public static bool Allows(IReadOnlyList<string> ranges, IPAddress? caller)
    {
        if (ranges.Count == 0) return true;
        if (caller is null) return false;

        var address = Unwrap(caller);

        return ranges.Any(range => Matches(range, address));
    }

    public static bool IsValidEntry(string entry) => TryParse(entry, out _, out _);

    private static bool Matches(string range, IPAddress caller)
    {
        if (!TryParse(range, out var network, out var prefixLength)) return false;
        if (network.AddressFamily != caller.AddressFamily) return false;

        var networkBytes = network.GetAddressBytes();
        var callerBytes = caller.GetAddressBytes();

        var wholeBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < wholeBytes; i++)
        {
            if (networkBytes[i] != callerBytes[i]) return false;
        }

        if (remainingBits == 0) return true;

        var mask = (byte)(0xFF << (8 - remainingBits));
        return (networkBytes[wholeBytes] & mask) == (callerBytes[wholeBytes] & mask);
    }

    private static bool TryParse(string entry, out IPAddress network, out int prefixLength)
    {
        network = IPAddress.None;
        prefixLength = 0;

        var trimmed = entry.Trim();
        if (trimmed.Length == 0) return false;

        var slash = trimmed.IndexOf('/');

        if (slash < 0)
        {
            if (!IPAddress.TryParse(trimmed, out var single)) return false;

            network = Unwrap(single);
            prefixLength = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
            return true;
        }

        if (!IPAddress.TryParse(trimmed[..slash], out var parsed)) return false;
        if (!int.TryParse(trimmed[(slash + 1)..], out var length)) return false;

        network = Unwrap(parsed);

        var maximum = network.AddressFamily == AddressFamily.InterNetwork ? 32 : 128;
        if (length < 0 || length > maximum) return false;

        prefixLength = length;
        return true;
    }

    private static IPAddress Unwrap(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
