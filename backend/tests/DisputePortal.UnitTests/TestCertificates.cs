using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DisputePortal.UnitTests;

/// <summary>Self-signed server certificates generated per test run, so no key material is ever committed.</summary>
internal static class TestCertificates
{
    private const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";

    /// <summary>
    /// A leaf shaped like the cluster's Vault certificate (CA:FALSE, server authentication) with SANs for
    /// <c>localhost</c> and 127.0.0.1. Re-imported from PKCS#12 so its key is not ephemeral and Kestrel can serve it.
    /// </summary>
    public static X509Certificate2 Server(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        CertificateRequest request = new("CN=vault.test", key, HashAlgorithmName.SHA256);
        SubjectAlternativeNameBuilder names = new();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, pathLengthConstraint: 0, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthentication)], critical: false));
        using X509Certificate2 ephemeral = request.CreateSelfSigned(notBefore, notAfter);
        return X509CertificateLoader.LoadPkcs12(ephemeral.Export(X509ContentType.Pfx), password: null);
    }

    /// <summary>Valid from a day ago for a year.</summary>
    public static X509Certificate2 Server() => Server(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
}
