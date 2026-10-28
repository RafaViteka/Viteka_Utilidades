using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Viteka.Server;

internal sealed class OracleProfile
{
    public OracleProfile() { }
    public int Schema { get; set; } = 1;
    public string Dsn { get; set; } = "NIXFARMA";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public void Validate()
    {
        if (Schema != 1 || string.IsNullOrWhiteSpace(Dsn) || Dsn.Length > 32 || Dsn.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(User) || User.Length > 128 || User.Any(char.IsControl) || string.IsNullOrEmpty(Password) || Password.Length > 1024) throw new InvalidDataException();
    }
}
internal sealed class OracleService(string directory)
{
    private readonly NixFarmaConnectionTester tester = new();
    private readonly SemaphoreSlim gate = new(1,1);
    private string FilePath => Path.Combine(directory, "oracle.dat");
    internal async Task<object> HandleAsync(string action, JsonElement data, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return new { busy = true, message = "Hay una operación de conexión en curso." };
        try
        {
            var settings = Read();
            switch (action)
            {
                case "state": return State(settings);
                case "configure":
                    var next = new OracleProfile { Dsn = data.GetProperty("dsn").GetString() ?? "", User = data.GetProperty("user").GetString() ?? "", Password = data.GetProperty("password").GetString() ?? "" };
                    if (data.TryGetProperty("keepPassword", out var keep) && keep.GetBoolean())
                    {
                        if (settings is null || settings.Dsn != next.Dsn || settings.User != next.User) throw new ArgumentException("La conexión ha cambiado. Vuelve a introducir la contraseña Oracle.");
                        next.Password = settings.Password;
                    }
                    next.Validate(); Save(next); return new { state = State(next) };
                case "forget":
                    File.Delete(FilePath); return new { state = State(null) };
                case "test":
                    if (settings is null) return new { success = false, code = "not_configured", message = "Configura primero la conexión Oracle del servidor." };
                    var result = await tester.TestAsync(settings, ct);
                    return new { success = result.Success, code = result.Code, message = result.Message, sqlState = result.SqlState, oracleCode = result.OracleCode, checkedAt = DateTimeOffset.Now };
                default: throw new ArgumentException();
            }
        }
        finally { gate.Release(); }
    }
    private object State(OracleProfile? profile)
    {
        bool available = false;
        if (OperatingSystem.IsWindows())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var sources = machine.OpenSubKey(@"SOFTWARE\ODBC\ODBC.INI\ODBC Data Sources");
            available = sources?.GetValue(profile?.Dsn ?? "NIXFARMA") is string;
        }
        return new { configured = profile is not null, saved = File.Exists(FilePath), dsn = profile?.Dsn ?? "NIXFARMA", user = profile?.User ?? "", dsnAvailable = available };
    }
    private OracleProfile? Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(FilePath)) return null;
        if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException();
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.LocalMachine);
        try { var result = JsonSerializer.Deserialize<OracleProfile>(clear) ?? throw new InvalidDataException(); result.Validate(); return result; }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private void Save(OracleProfile settings)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings); byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.LocalMachine); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
