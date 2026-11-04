using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pigeon.Service.Peers;

// The long-term identity of this Pigeon service toward its peers: an ECDSA P-256 key in a
// self-signed certificate. Peers pin its fingerprint at pairing; there is no certificate authority.
// The key never leaves the store and is never printed.
public sealed class ServiceIdentity : IDisposable {
    public X509Certificate2 Certificate { get; }
    public string Fingerprint { get; }

    private ServiceIdentity(X509Certificate2 certificate) {
        Certificate = certificate;
        Fingerprint = FingerprintOf(certificate);
    }

    // SHA-256 of the public key (SubjectPublicKeyInfo), so it survives certificate renewal.
    public static string FingerprintOf(X509Certificate2 certificate)
        => Convert.ToHexStringLower(SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo()));

    public static ServiceIdentity LoadOrCreate(IIdentityStore store) {
        var existing = store.Load();
        if (existing is not null && existing.HasPrivateKey)
            return new ServiceIdentity(existing);
        existing?.Dispose();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=Pigeon service", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        // Both roles: it serves the peer listener and presents itself when connecting out.
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")], false));
        using var created = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(20));
        return new ServiceIdentity(store.Save(created));
    }

    public void Dispose() => Certificate.Dispose();
}

public interface IIdentityStore {
    // The stored certificate with its private key, or null if there is none.
    X509Certificate2? Load();
    // Persists the certificate and its private key, returning the persisted copy.
    X509Certificate2 Save(X509Certificate2 certificate);
}

// The current user's certificate store: CNG keys protected by DPAPI on Windows (not exportable),
// the login Keychain on macOS, and a user-only directory on Linux. The thumbprint of Pigeon's
// certificate is kept next to the database, so other certificates in the store are never touched.
public sealed class UserCertificateStore(string thumbprintFile) : IIdentityStore {
    public X509Certificate2? Load() {
        if (!File.Exists(thumbprintFile))
            return null;
        var thumbprint = File.ReadAllText(thumbprintFile).Trim();
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, false);
        X509Certificate2? match = found.Count == 1 ? found[0] : null;
        foreach (var other in found)
            if (!ReferenceEquals(other, match))
                other.Dispose();
        return match;
    }

    public X509Certificate2 Save(X509Certificate2 certificate) {
        var flags = X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.PersistKeySet;
        // macOS keeps the key in the Keychain only when it is exportable while being added.
        if (OperatingSystem.IsMacOS())
            flags |= X509KeyStorageFlags.Exportable;
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        var persisted = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx, password), password, flags);
        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser)) {
            store.Open(OpenFlags.ReadWrite);
            store.Add(persisted);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(thumbprintFile)!);
        File.WriteAllText(thumbprintFile, persisted.Thumbprint);
        return persisted;
    }
}

// A PKCS#12 file with a random password in a sibling file, for tests and isolated runs only. Not an
// ephemeral key set: Windows TLS (SChannel) cannot use ephemeral keys.
public sealed class FileIdentityStore(string directory) : IIdentityStore {
    private string Pfx => Path.Combine(directory, "identity.pfx");
    private string Password => Path.Combine(directory, "identity.key");

    public X509Certificate2? Load()
        => File.Exists(Pfx) && File.Exists(Password)
            ? X509CertificateLoader.LoadPkcs12FromFile(Pfx, File.ReadAllText(Password), X509KeyStorageFlags.UserKeySet)
            : null;

    public X509Certificate2 Save(X509Certificate2 certificate) {
        Directory.CreateDirectory(directory);
        var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        File.WriteAllText(Password, password);
        File.WriteAllBytes(Pfx, certificate.Export(X509ContentType.Pfx, password));
        return Load()!;
    }
}
