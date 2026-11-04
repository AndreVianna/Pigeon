using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pigeon.Service.Peers;

// A one-time pairing invitation (see docs/remote-design.md): where to reach the inviting service,
// the fingerprint its certificate must have, its proposed name, and a random secret. It is sent
// between people over a channel they trust, is valid for 10 minutes and for one acceptance.
public sealed record Invitation(string Address, string Fingerprint, string Name, byte[] Secret) {
    private const string Prefix = "pigeon1.";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private sealed record Wire(string A, string F, string N, string S);

    public string Encode() => Prefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Wire(Address, Fingerprint, Name, Convert.ToBase64String(Secret))))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static Invitation Decode(string code) {
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.Ordinal) || code.Length > 2048)
            throw new ArgumentException("This is not a Pigeon invitation.");
        try {
            var body = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            body += new string('=', (4 - body.Length % 4) % 4);
            var wire = JsonSerializer.Deserialize<Wire>(Convert.FromBase64String(body))!;
            var secret = Convert.FromBase64String(wire.S);
            if (secret.Length != 32 || wire.F.Length != 64 || !wire.F.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(wire.A) || string.IsNullOrWhiteSpace(wire.N))
                throw new FormatException();
            return new Invitation(wire.A, wire.F, wire.N, secret);
        }
        catch (Exception e) when (e is FormatException or JsonException or NullReferenceException) {
            throw new ArgumentException("This Pigeon invitation is damaged; ask for a new one.");
        }
    }

    // Proves the acceptor holds the secret, bound to both services, so it cannot be replayed elsewhere.
    public static string Proof(byte[] secret, string inviter, string acceptor)
        => Convert.ToHexStringLower(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"pigeon-pair-proof|{inviter}|{acceptor}")));

    // The 6 digits both people compare; a substituted acceptor or inviter produces different digits.
    public static string CheckCode(byte[] secret, string inviter, string acceptor) {
        var mac = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"pigeon-pair-check|{inviter}|{acceptor}"));
        return (BinaryPrimitives.ReadUInt32BigEndian(mac) % 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }
}
