using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Viteka.Core;

public sealed record AccessBootstrap(int Schema, string ServerUrl, string RootSha256);
public sealed class AccessServiceTrust : IDisposable
{
    private readonly X509Certificate2 root;
    public Uri Server { get; }
    public HttpClient Client { get; }
    public AccessServiceTrust(string bootstrapPath)
    {
        if (new FileInfo(bootstrapPath).Length > 16_384) throw new InvalidDataException();
        var settings = JsonSerializer.Deserialize<AccessBootstrap>(File.ReadAllBytes(bootstrapPath)) ?? throw new InvalidDataException();
        if (settings.Schema != 1 || string.IsNullOrEmpty(settings.RootSha256) || !System.Text.RegularExpressions.Regex.IsMatch(settings.RootSha256, "^[A-Fa-f0-9]{64}$") || !Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) throw new InvalidDataException("Destino de acceso inválido.");
        var certPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bootstrapPath))!, "access-root.cer");
        root = X509CertificateLoader.LoadCertificateFromFile(certPath);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(root.RawData), Convert.FromHexString(settings.RootSha256)))
            throw new InvalidDataException("La identidad del servidor no coincide con la configurada.");
        // Local bootstrap remains on loopback, even when the public hostname resolves to a LAN address.
        Server = uri.Host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? new UriBuilder(uri) { Host = "localhost" }.Uri : uri;
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, errors) =>
        {
            if (certificate is null || (errors & (SslPolicyErrors.RemoteCertificateNameMismatch | SslPolicyErrors.RemoteCertificateNotAvailable)) != 0) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            chain.ChainPolicy.CustomTrustStore.Add(root);
            // This installation-specific CA has no external CRL service. Trust is pinned
            // to its protected bootstrap; chain signatures, hostname, EKU and dates still apply.
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            return chain.Build(certificate);
        };
        Client = new HttpClient(handler) { BaseAddress = Server, Timeout = TimeSpan.FromSeconds(20) };
    }
    public void Dispose() { Client.Dispose(); root.Dispose(); }
}
