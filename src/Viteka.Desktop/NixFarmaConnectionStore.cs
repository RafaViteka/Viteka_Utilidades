using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace Viteka.Desktop;

internal sealed class NixFarmaSettings
{
    public NixFarmaSettings() { }
    public int Schema { get; set; } = 1;
    public string Dsn { get; set; } = "NIXFARMA";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public void Validate(bool allowEmptyPassword = false)
    {
        if (Schema != 1 || string.IsNullOrWhiteSpace(Dsn) || Dsn.Length > 32 || Dsn.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(User) || User.Length > 128 || User.Any(char.IsControl) ||
            (!allowEmptyPassword && string.IsNullOrEmpty(Password)) || Password is null || Password.Length > 1024)
            throw new InvalidDataException("Invalid connection configuration.");
    }
    // Default object ToString deliberately does not include any fields.
}

internal sealed class NixFarmaConnectionStore(string directory)
{
    internal string FilePath => Path.Combine(directory, "nixfarma.dat");
    internal bool Exists => File.Exists(FilePath);
    internal NixFarmaSettings? Load()
    {
        if (!Exists) return null;
        if (new FileInfo(FilePath).Length > 65_536) throw new InvalidDataException();
        var encrypted = File.ReadAllBytes(FilePath);
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try
        {
            var result = JsonSerializer.Deserialize<NixFarmaSettings>(clear) ?? throw new InvalidDataException();
            result.Validate();
            return result;
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    internal void Save(NixFarmaSettings settings)
    {
        settings.Validate();
        // Preserve an unreadable/future configuration until the user explicitly forgets it.
        if (Exists) _ = Load();
        Directory.CreateDirectory(directory);
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temporary = Path.Combine(directory, $".nixfarma-{Guid.NewGuid():N}.tmp");
        try { File.WriteAllBytes(temporary, encrypted); File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal void Forget() => File.Delete(FilePath);
}
