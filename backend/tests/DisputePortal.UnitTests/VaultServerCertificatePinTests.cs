using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using DisputePortal.Api.Shared.Secrets;

namespace DisputePortal.UnitTests;

/// <summary>
/// The pin decision (exact certificate, host name, validity dates; chain errors ignored) and reading the pin file:
/// exactly one PEM or DER certificate, anything else fails fast as misconfiguration.
/// </summary>
public sealed class VaultServerCertificatePinTests : IDisposable
{
    // A non-UTC offset, so comparing local-kind NotBefore/NotAfter without converting to UTC would show.
    private static readonly TimeSpan Offset = TimeSpan.FromHours(-7);

    private static readonly DateTimeOffset ValidFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidUntil = new(2027, 3, 16, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, Offset);

    // Generated once for the class: on macOS each PKCS#12 import costs about half a second.
    private static readonly X509Certificate2 Pinned = TestCertificates.Server(ValidFrom, ValidUntil);
    private static readonly X509Certificate2 Other = TestCertificates.Server(ValidFrom, ValidUntil);
    private readonly string directory = Directory.CreateTempSubdirectory("vault-pin-test").FullName;

    private static byte[] PinnedHash => Pinned.GetCertHash(HashAlgorithmName.SHA256);

    [Fact]
    public void ThePinnedCertificateIsAccepted() =>
        Assert.True(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.None, PinnedHash, Now));

    // The live case: Vault's leaf is issued by a CA that is not published, so the chain never builds.
    [Fact]
    public void ChainErrorsAloneAreAcceptedWhenThePinNameAndDatesMatch() =>
        Assert.True(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.RemoteCertificateChainErrors, PinnedHash, Now));

    [Theory]
    [InlineData(SslPolicyErrors.None)]
    [InlineData(SslPolicyErrors.RemoteCertificateChainErrors)]
    public void ADifferentCertificateIsRejected(SslPolicyErrors errors) =>
        Assert.False(VaultServerCertificatePin.Accepts(Other, errors, PinnedHash, Now));

    // The pin replaces the chain check only: a host name that is not in the certificate's SANs is still refused.
    [Theory]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch)]
    [InlineData(SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateChainErrors)]
    public void ANameMismatchIsRejectedEvenWithTheRightPin(SslPolicyErrors errors) =>
        Assert.False(VaultServerCertificatePin.Accepts(Pinned, errors, PinnedHash, Now));

    [Fact]
    public void AnExpiredCertificateIsRejected() =>
        Assert.False(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.RemoteCertificateChainErrors, PinnedHash, ValidUntil.ToOffset(Offset).AddMinutes(1)));

    [Fact]
    public void ANotYetValidCertificateIsRejected() =>
        Assert.False(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.RemoteCertificateChainErrors, PinnedHash, ValidFrom.ToOffset(Offset).AddMinutes(-1)));

    // Pairs with the two tests above: one minute inside either end is still accepted.
    [Fact]
    public void OneMinuteInsideEitherEndIsAccepted()
    {
        Assert.True(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.None, PinnedHash, ValidUntil.ToOffset(Offset).AddMinutes(-1)));
        Assert.True(VaultServerCertificatePin.Accepts(Pinned, SslPolicyErrors.None, PinnedHash, ValidFrom.ToOffset(Offset).AddMinutes(1)));
    }

    [Fact]
    public void NoCertificateIsRejected() =>
        Assert.False(VaultServerCertificatePin.Accepts(null, SslPolicyErrors.RemoteCertificateNotAvailable, PinnedHash, Now));

    // SHA-256 of the DER encoding, not the SHA-1 Thumbprint.
    [Fact]
    public void APemFileReadsAsTheSha256OfTheCertificate() =>
        Assert.Equal(PinnedHash, VaultServerCertificatePin.ReadHash(Write("vault.pem", Pinned.ExportCertificatePem())));

    [Fact]
    public void ADerFileReadsAsTheSameHash() =>
        Assert.Equal(PinnedHash, VaultServerCertificatePin.ReadHash(Write("vault.der", Pinned.RawData)));

    [Fact]
    public void AMissingFileIsMisconfiguration()
    {
        string path = Path.Combine(directory, "absent.pem");

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() => VaultServerCertificatePin.ReadHash(path));

        Assert.Contains("PinnedServerCertificateFile", failure.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<IOException>(failure.InnerException);
    }

    [Fact]
    public void AGarbageFileIsMisconfiguration()
    {
        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            VaultServerCertificatePin.ReadHash(Write("garbage.pem", "this is not a certificate\n")));

        Assert.Contains("is not an X.509 certificate", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoPemCertificatesAreMisconfiguration()
    {
        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            VaultServerCertificatePin.ReadHash(Write("bundle.pem", Pinned.ExportCertificatePem() + "\n" + Other.ExportCertificatePem())));

        Assert.Contains("exactly one certificate", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoConcatenatedDerCertificatesAreMisconfiguration()
    {
        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            VaultServerCertificatePin.ReadHash(Write("bundle.der", [.. Pinned.RawData, .. Other.RawData])));

        Assert.Contains("exactly one certificate", failure.Message, StringComparison.Ordinal);
    }

    // A key (or anything else) where the certificate should be is refused, and the message never echoes the file.
    [Fact]
    public void APemBlockThatIsNotACertificateIsMisconfiguration()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string keyPem = key.ExportPkcs8PrivateKeyPem();

        VaultMisconfiguredException failure = Assert.Throws<VaultMisconfiguredException>(() =>
            VaultServerCertificatePin.ReadHash(Write("key.pem", keyPem)));

        Assert.Contains("exactly one PEM certificate", failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", failure.ToString(), StringComparison.Ordinal);
    }

    private string Write(string name, string content) => Write(name, System.Text.Encoding.ASCII.GetBytes(content));

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
