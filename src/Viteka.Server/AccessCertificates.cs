using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Viteka.Core;

namespace Viteka.Server;

internal static class AccessCertificates
{
    internal static X509Certificate2 LoadOrCreate(string privateDirectory, string publicDirectory, int port)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        Directory.CreateDirectory(privateDirectory); Directory.CreateDirectory(publicDirectory);
        var rootPath = Path.Combine(privateDirectory, "root-key.dat");
        X509Certificate2 root;
        if (File.Exists(rootPath)) root = LoadProtected(rootPath);
        else
        {
            if (File.Exists(Path.Combine(privateDirectory, "access.initialized"))) throw new InvalidDataException("Falta la identidad TLS. No se regenerará automáticamente.");
            using var key = RSA.Create(3072);
            var request = new CertificateRequest("CN=Viteka Utilidades - Autoridad local", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            root = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddYears(10));
            SaveProtected(rootPath, root);
        }
        using (root)
        {
            var leafPath = Path.Combine(privateDirectory, "server-key.dat");
            X509Certificate2? leaf = File.Exists(leafPath) ? LoadProtected(leafPath) : null;
            if (leaf is null || leaf.NotAfter.ToUniversalTime() < DateTime.UtcNow.AddDays(30) || !leaf.GetNameInfo(X509NameType.SimpleName, false).Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                leaf?.Dispose();
                using var key = RSA.Create(2048);
                var request = new CertificateRequest($"CN={Environment.MachineName}", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
                var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddDnsName(Environment.MachineName);
                names.AddIpAddress(IPAddress.Loopback); names.AddIpAddress(IPAddress.IPv6Loopback);
                request.CertificateExtensions.Add(names.Build());
                using var signed = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1), RandomNumberGenerator.GetBytes(16));
                leaf = signed.CopyWithPrivateKey(key); SaveProtected(leafPath, leaf);
            }
            AtomicWrite(Path.Combine(publicDirectory, "access-root.cer"), root.Export(X509ContentType.Cert));
            var metadata = new AccessBootstrap(1, $"https://{Environment.MachineName}:{port}/", Convert.ToHexString(SHA256.HashData(root.RawData)));
            AtomicWrite(Path.Combine(publicDirectory, "access-server.json"), JsonSerializer.SerializeToUtf8Bytes(metadata));
            return leaf;
        }
    }
    private static X509Certificate2 LoadProtected(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(path), null, DataProtectionScope.LocalMachine);
        try { return X509CertificateLoader.LoadPkcs12(clear, null, X509KeyStorageFlags.EphemeralKeySet); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void SaveProtected(string path, X509Certificate2 cert)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var clear = cert.Export(X509ContentType.Pkcs12);
        try { AtomicWrite(path, ProtectedData.Protect(clear, null, DataProtectionScope.LocalMachine)); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private static void AtomicWrite(string path, byte[] data)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temporary, data); File.Move(temporary, path, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
