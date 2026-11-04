using System.Security.Cryptography.X509Certificates;

using Pigeon.Service.Peers;

using Xunit;

namespace Pigeon.UnitTests;

// Uses a file store in a temp folder, never the user's certificate store.
public sealed class ServiceIdentityTests : IDisposable {
    private readonly string directory = Path.Combine(Path.GetTempPath(), "pigeon-identity-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void An_identity_is_created_once_and_loaded_with_the_same_fingerprint_afterwards() {
        string first;
        using (var created = ServiceIdentity.LoadOrCreate(new FileIdentityStore(directory))) {
            first = created.Fingerprint;
            Assert.True(created.Certificate.HasPrivateKey);
            Assert.Matches("^[0-9a-f]{64}$", first);
        }

        using var loaded = ServiceIdentity.LoadOrCreate(new FileIdentityStore(directory));
        Assert.Equal(first, loaded.Fingerprint);
        Assert.True(loaded.Certificate.HasPrivateKey);
    }

    [Fact]
    public void The_certificate_is_a_P256_key_usable_as_both_server_and_client() {
        using var identity = ServiceIdentity.LoadOrCreate(new FileIdentityStore(directory));
        using var key = identity.Certificate.GetECDsaPublicKey();
        Assert.NotNull(key);
        Assert.Equal(256, key.KeySize);
        var usages = identity.Certificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().Single().EnhancedKeyUsages.Cast<System.Security.Cryptography.Oid>().Select(o => o.Value);
        Assert.Equal(["1.3.6.1.5.5.7.3.1", "1.3.6.1.5.5.7.3.2"], usages);
    }

    [Fact]
    public void Two_services_have_different_fingerprints() {
        using var a = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "a")));
        using var b = ServiceIdentity.LoadOrCreate(new FileIdentityStore(Path.Combine(directory, "b")));
        Assert.NotEqual(a.Fingerprint, b.Fingerprint);
    }

    public void Dispose() {
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
