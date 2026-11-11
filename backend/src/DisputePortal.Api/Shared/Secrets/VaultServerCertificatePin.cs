using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DisputePortal.Api.Shared.Secrets;

/// <summary>
/// Trusts exactly one Vault server certificate instead of a CA chain (ADR-016). Needed when Vault's certificate is
/// self-issued by a CA that is not published anywhere, so .NET can never build a chain to a root (PartialChain).
/// </summary>
public static class VaultServerCertificatePin
{
    private const string CertificateLabel = "CERTIFICATE";

    /// <summary>
    /// Reads the pinned certificate (one PEM or DER certificate) and returns the SHA-256 hash of its DER encoding.
    /// Every failure is a <see cref="VaultMisconfiguredException"/>: this is security configuration, never retried.
    /// The certificate's contents are never part of a message.
    /// </summary>
    public static byte[] ReadHash(string path)
    {
        byte[] file = ReadFile(path);
        byte[] der = SingleCertificateDer(file, path);
        try
        {
            using X509Certificate2 certificate = X509CertificateLoader.LoadCertificate(der);
            // The loader stops after the first DER structure; trailing bytes mean a second certificate or junk.
            if (certificate.RawData.Length != der.Length)
            {
                throw Misconfigured(path, "must contain exactly one certificate");
            }

            return certificate.GetCertHash(HashAlgorithmName.SHA256);
        }
        catch (CryptographicException exception)
        {
            throw Misconfigured(path, "is not an X.509 certificate", exception);
        }
    }

    /// <summary>
    /// Accepts the presented certificate iff it is the pinned one, the host name matches it, and it is valid now.
    /// Chain errors alone are ignored: the pin replaces CA-chain validation, the name and date checks do not.
    /// </summary>
    public static bool Accepts(X509Certificate2? certificate, SslPolicyErrors errors, ReadOnlySpan<byte> pinnedHash, DateTimeOffset now)
    {
        const SslPolicyErrors Fatal = SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable;
        if (certificate is null || (errors & Fatal) != SslPolicyErrors.None)
        {
            return false;
        }

        // NotBefore and NotAfter are local-time values; compare in UTC so the machine's time zone cannot matter.
        DateTime utcNow = now.UtcDateTime;
        if (utcNow < certificate.NotBefore.ToUniversalTime() || utcNow > certificate.NotAfter.ToUniversalTime())
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(certificate.GetCertHash(HashAlgorithmName.SHA256), pinnedHash);
    }

    /// <summary>Installs the pin on VaultSharp's own handler, which it passes to <c>PostProcessHttpClientHandlerAction</c>.</summary>
    public static void Apply(HttpMessageHandler handler, byte[] pinnedHash)
    {
        if (handler is not HttpClientHandler clientHandler)
        {
            throw new InvalidOperationException($"Vault's HTTP handler is a {handler.GetType().Name}, so the certificate pin cannot be installed");
        }

        clientHandler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
            Accepts(certificate, errors, pinnedHash, DateTimeOffset.UtcNow);
    }

    private static byte[] ReadFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Misconfigured(path, "cannot be read", exception);
        }
    }

    // A PEM file must hold exactly one block, and it must be a certificate (a key or a bundle is refused).
    // Anything without a PEM header is treated as DER.
    private static byte[] SingleCertificateDer(byte[] file, string path)
    {
        ReadOnlySpan<char> text = System.Text.Encoding.ASCII.GetString(file);
        if (!text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            return file;
        }

        if (!PemEncoding.TryFind(text, out PemFields first) || !text[first.Label].SequenceEqual(CertificateLabel))
        {
            throw Misconfigured(path, "must contain exactly one PEM certificate");
        }

        if (PemEncoding.TryFind(text[first.Location.End..], out _))
        {
            throw Misconfigured(path, "must contain exactly one certificate");
        }

        return Convert.FromBase64String(text[first.Base64Data].ToString());
    }

    private static VaultMisconfiguredException Misconfigured(string path, string problem, Exception? cause = null) =>
        new($"Vault:PinnedServerCertificateFile {path} {problem}", cause);
}
