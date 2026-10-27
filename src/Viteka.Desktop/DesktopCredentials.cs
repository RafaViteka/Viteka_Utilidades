using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Viteka.Contracts;
namespace Viteka.Desktop;

// DPAPI CurrentUser: bound to this Windows user on this machine, not to the shared executable.
public sealed class DesktopCredentials(string directory)
{
    private string CredentialPath => Path.Combine(directory, "device.dat");
    public PairingResponse? Load()
    {
        if (!File.Exists(CredentialPath)) return null;
        var encrypted = File.ReadAllBytes(CredentialPath);
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            var credential = JsonSerializer.Deserialize<PairingResponse>(clear);
            return credential is not null && credential.ExpiresAt > DateTimeOffset.UtcNow ? credential : null;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    public void Save(PairingResponse credential)
    {
        Directory.CreateDirectory(directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(credential);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temporary = Path.Combine(directory, $".credential-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, CredentialPath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
