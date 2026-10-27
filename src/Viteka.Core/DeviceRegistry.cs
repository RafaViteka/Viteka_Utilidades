using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Viteka.Core;

public sealed record DeviceRegistration(string Id, string Name, string TokenHash, DateTimeOffset ExpiresAt, bool Revoked);
public sealed record PairingGrant(string CodeHash, DateTimeOffset ExpiresAt);
public sealed record SecurityState(int Version, DeviceRegistration[] Devices, PairingGrant? PendingGrant);
public sealed record IssuedCredential(string DeviceId, string Token, DateTimeOffset ExpiresAt);

// Kept outside the shared application folder. Raw codes and tokens are never stored in this registry.
public sealed class DeviceRegistry(string directory, TimeProvider? timeProvider = null)
{
    private readonly string directory = Path.GetFullPath(directory);
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private string StatePath => Path.Combine(directory, "devices.json");

    public string IssueCode() => Transaction(state =>
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return (state with { PendingGrant = new PairingGrant(Hash(code), clock.GetUtcNow().AddMinutes(5)) }, code);
    });

    public IssuedCredential? Redeem(string? code, string? name) => Transaction<IssuedCredential?>(state =>
    {
        if (code is null || code.Length != 32 || name is null || name.Length is < 1 or > 64 ||
            name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            return (state, null);
        var grant = state.PendingGrant;
        if (grant is null || grant.ExpiresAt <= clock.GetUtcNow() || !Matches(code, grant.CodeHash)) return (state, null);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var id = Guid.NewGuid().ToString("N");
        var expiry = clock.GetUtcNow().AddDays(90);
        var device = new DeviceRegistration(id, name, Hash(token), expiry, false);
        return (state with { PendingGrant = null, Devices = state.Devices.Append(device).ToArray() },
            new IssuedCredential(id, token, expiry));
    });

    public bool IsAuthorized(string? token)
    {
        if (token is null || token.Length != 64) return false;
        return Transaction(state => (state, state.Devices.Any(x => !x.Revoked && x.ExpiresAt > clock.GetUtcNow() && Matches(token, x.TokenHash))));
    }

    public bool Revoke(string id) => Transaction(state =>
    {
        var found = state.Devices.Any(x => x.Id == id);
        return (state with { Devices = state.Devices.Select(x => x.Id == id ? x with { Revoked = true } : x).ToArray() }, found);
    });

    public IReadOnlyList<DeviceRegistration> ListDevices() => Transaction(state => (state, (IReadOnlyList<DeviceRegistration>)state.Devices));

    private T Transaction<T>(Func<SecurityState, (SecurityState State, T Result)> operation)
    {
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var gate = AcquireLock();
        var state = File.Exists(StatePath)
            ? JsonSerializer.Deserialize<SecurityState>(File.ReadAllText(StatePath)) ?? throw new InvalidDataException("Registro de puestos vacío.")
            : new SecurityState(1, [], null);
        if (state.Version != 1 || state.Devices is null) throw new InvalidDataException("Registro de puestos no compatible.");
        var (next, result) = operation(state);
        if (next != state)
        {
            var temporary = Path.Combine(directory, $".devices-{Guid.NewGuid():N}.tmp");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (var file = new FileStream(temporary, options))
                {
                    JsonSerializer.Serialize(file, next);
                    file.Flush(true);
                }
                File.Move(temporary, StatePath, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        return result;
    }

    private FileStream AcquireLock()
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(directory, "devices.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 20) { Thread.Sleep(25); }
        }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static bool Matches(string raw, string hash)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Hash(raw)), Convert.FromHexString(hash)); }
        catch (FormatException) { return false; }
    }
}
