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
    public string? ValidationFailure { get; private set; }
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
        if(ProductProfile.IsReview && (!Server.IsLoopback || Server.Port!=ProductProfile.AccessPort))throw new InvalidDataException("La edición de pruebas solo conecta con su servicio local de pruebas.");
        var policy = new X509ChainPolicy
        {
            TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck,
            DisableCertificateDownloads = true,
            UrlRetrievalTimeout = TimeSpan.FromSeconds(2)
        };
        policy.CustomTrustStore.Add(root); policy.ExtraStore.Add(root);
        policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        // Configure the chain before SslStream validation, rather than rebuilding it
        // after the OS has tried to discover this private installation's issuer.
        var handler = new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
        handler.SslOptions.CertificateChainPolicy = policy;
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, chain, errors) =>
        {
            if (certificate is null || errors != SslPolicyErrors.None)
            {
                ValidationFailure = errors + "; " + string.Join(",", chain?.ChainStatus.Select(s => s.Status.ToString()) ?? []);
                return false;
            }
            return true;
        };
        Client = new HttpClient(handler) { BaseAddress = Server, Timeout = TimeSpan.FromSeconds(20) };
    }
    public void Dispose() { Client.Dispose(); root.Dispose(); }
}
