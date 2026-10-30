using System.Text.Json;

namespace Viteka.Core;

/// <summary>
/// Sesión persistida del servicio de acceso. Solo se guarda el hash SHA-256 del token, nunca el token.
/// La validez real se vuelve a comprobar contra <see cref="AccessDirectory"/> (usuario activo y versión de contraseña).
/// </summary>
public sealed record PersistedAccessSession(string TokenHash, string UserId, string PasswordVersion, DateTimeOffset Expires);

/// <summary>
/// Conserva las sesiones abiertas cuando el servicio se reinicia (por ejemplo, durante una actualización),
/// para que los usuarios no tengan que volver a iniciar sesión. El contenido se protege con el callback del host
/// (DPAPI de máquina en Windows).
/// </summary>
public sealed class AccessSessionStore(string directory, Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, TimeProvider? clock = null)
{
    private const int MaximumSessions = 2000;
    private const long MaximumFileSize = 2 * 1024 * 1024;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly object gate = new();

    private string FilePath => Path.Combine(directory, "sessions.dat");

    public IReadOnlyList<PersistedAccessSession> Load()
    {
        lock (gate)
        {
            try
            {
                if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > MaximumFileSize) return [];
                var clear = unprotect(File.ReadAllBytes(FilePath));
                var items = JsonSerializer.Deserialize<PersistedAccessSession[]>(clear) ?? [];
                var now = time.GetUtcNow();
                return items
                    .Where(s => s is not null && IsHash(s.TokenHash) && Guid.TryParseExact(s.UserId, "N", out _)
                        && !string.IsNullOrEmpty(s.PasswordVersion) && s.Expires > now && s.Expires <= now.AddHours(8.5))
                    .Take(MaximumSessions)
                    .ToArray();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException
                or System.Security.Cryptography.CryptographicException)
            {
                // Un archivo dañado o de otra máquina no debe impedir arrancar: los usuarios simplemente vuelven a entrar.
                return [];
            }
        }
    }

    public void Save(IEnumerable<PersistedAccessSession> sessions)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            var items = sessions.Where(s => s.Expires > now).Take(MaximumSessions).ToArray();
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".sessions-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, protect(JsonSerializer.SerializeToUtf8Bytes(items)));
                File.Move(temporary, FilePath, true);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static bool IsHash(string? value) => value is { Length: 64 } && value.All(Uri.IsHexDigit);
}
