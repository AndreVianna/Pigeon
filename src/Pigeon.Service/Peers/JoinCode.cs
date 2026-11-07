using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pigeon.Service.Peers;

// The join code of a first contact (see docs/remote-design.md). Each side fixes a random value before
// it sees the other's: the requester first sends only a hash of its value, the listing side answers
// with its own, then the requester reveals its value. The 6 digits depend on both values, both
// certificates and the topic, so neither side nor a machine relaying between them can choose them.
public static class JoinCode {
    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(32);

    public static string Commit(byte[] nonce) => Convert.ToHexStringLower(SHA256.HashData(nonce));

    public static bool Matches(string commitment, byte[] nonce)
        => commitment.Length == 64 && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Commit(nonce)), Encoding.ASCII.GetBytes(commitment));

    public static string Derive(byte[] requesterNonce, byte[] listingNonce, string listing, string requester, string topic) {
        var mac = HMACSHA256.HashData((byte[])[.. requesterNonce, .. listingNonce], Encoding.UTF8.GetBytes($"pigeon-join|{listing}|{requester}|{topic}"));
        return (BinaryPrimitives.ReadUInt32BigEndian(mac) % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    // A code for a local topic that requires approval; both sessions are on this machine.
    public static string Local() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static bool Same(string expected, string? given)
        => given is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(given.Trim()));
}
