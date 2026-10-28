using System.Security.Cryptography.X509Certificates;

namespace TenantPlatform.Web.Security.OAuth;

public sealed class OAuthIssuerOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public string Resource { get; set; } = "";
    public string ConnectionString { get; set; } = "";
    public string SigningCertificatePath { get; set; } = "";
    public string SigningCertificatePassword { get; set; } = "";
    public string EncryptionCertificatePath { get; set; } = "";
    public string EncryptionCertificatePassword { get; set; } = "";
    public string CookieKeyDirectory { get; set; } = "";
    public List<OAuthCertificateOptions> PreviousSigningCertificates { get; set; } = [];
    public List<OAuthCertificateOptions> PreviousEncryptionCertificates { get; set; } = [];
    public List<OAuthClientOptions> Clients { get; set; } = [];
    public const string ReadScope = "tenantplatform.agreements.read";

    public (X509Certificate2 Signing, X509Certificate2 Encryption, X509Certificate2[] PreviousSigning, X509Certificate2[] PreviousEncryption) Validate()
    {
        static bool Https(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo);
        if (!Https(Issuer) || !Https(Resource) || string.IsNullOrWhiteSpace(ConnectionString) || !Path.IsPathFullyQualified(CookieKeyDirectory))
            throw new InvalidOperationException("OAuth requires fixed HTTPS issuer/resource, dedicated database configuration and an absolute cookie key directory.");
        if (Clients.Select(x => x.ClientId).Distinct(StringComparer.Ordinal).Count() != Clients.Count)
            throw new InvalidOperationException("Duplicate OAuth client registration.");
        foreach (var client in Clients)
            if (string.IsNullOrWhiteSpace(client.ClientId) || string.IsNullOrWhiteSpace(client.DisplayName) || client.Secret.Length < 32 ||
                client.RedirectUris.Count == 0 || client.RedirectUris.Any(x => !Https(x) || x.Contains('*')))
                throw new InvalidOperationException("OAuth clients require names, secrets and exact HTTPS callbacks.");
        static X509Certificate2 Load(string path, string password, bool primary = true)
        {
            if (!Path.IsPathFullyQualified(path)) throw new InvalidOperationException("OAuth certificate paths must be absolute.");
            var cert = X509CertificateLoader.LoadPkcs12FromFile(path, password, OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet);
            using var rsa = cert.GetRSAPrivateKey();
            if (!cert.HasPrivateKey || rsa is null || rsa.KeySize < 2048 || primary &&
                (cert.NotAfter.ToUniversalTime() <= DateTime.UtcNow || cert.NotBefore.ToUniversalTime() > DateTime.UtcNow))
                throw new InvalidOperationException("OAuth requires currently valid RSA certificates with private keys.");
            return cert;
        }
        var signing = Load(SigningCertificatePath, SigningCertificatePassword);
        var encryption = Load(EncryptionCertificatePath, EncryptionCertificatePassword);
        if (signing.GetPublicKey().AsSpan().SequenceEqual(encryption.GetPublicKey())) throw new InvalidOperationException("Use distinct OAuth signing and encryption keys.");
        return (signing, encryption, PreviousSigningCertificates.Select(x => Load(x.Path, x.Password, false)).ToArray(),
            PreviousEncryptionCertificates.Select(x => Load(x.Path, x.Password, false)).ToArray());
    }
}
public sealed class OAuthCertificateOptions
{
    public string Path { get; set; } = "";
    public string Password { get; set; } = "";
}
public sealed class OAuthClientOptions
{
    public bool AllowServiceRequests { get; set; }
    public bool AllowStructure { get; set; }
    public bool AllowPersonalPages { get; set; }
    public string ClientId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Secret { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
}
