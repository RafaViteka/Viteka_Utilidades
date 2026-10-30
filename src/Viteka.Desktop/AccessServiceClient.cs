using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Viteka.Core;

namespace Viteka.Desktop;

internal sealed class AccessServiceClient : IDisposable
{
    private readonly AccessServiceTrust trust;
    private string? token;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    internal AccessServiceClient()
    {
        var bootstrap = App.SmokeTest ? Environment.GetEnvironmentVariable("VITEKA_ACCESS_BOOTSTRAP") : null;
        if (App.SmokeTest && (string.IsNullOrEmpty(bootstrap) || !Path.GetFullPath(bootstrap).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("El smoke requiere un bootstrap explícito en el directorio temporal, nunca la configuración de producción.");
        trust = new AccessServiceTrust(bootstrap ?? System.IO.Path.Combine(AppContext.BaseDirectory, "access-server.json"));
        trust.Client.Timeout = Timeout.InfiniteTimeSpan;
    }
    internal async Task<AccessUserView> RequireUserAsync(bool allowFirstPassword = false)
    {
        var state = await RequestAsync(HttpMethod.Get, "access/me");
        var user = state.GetProperty("user").Deserialize<AccessUserView>(Json) ?? throw new AccessSessionException();
        if (user.MustChangePassword && !allowFirstPassword) throw new AccessDeniedException("Cambia la contraseña inicial antes de continuar.");
        return user;
    }
    internal async Task<object> HandleAsync(string command, JsonElement data)
    {
        if (command == "auth.state")
        {
            if (token is null) return new { user = (object?)null };
            try { return await RequestAsync(HttpMethod.Get, "access/me"); }
            catch (AccessSessionException) { return new { user = (object?)null }; }
        }
        if (command == "auth.login")
        {
            token = null;
            var response = await RequestAsync(HttpMethod.Post, "access/login", data);
            token = response.GetProperty("token").GetString() ?? throw new AccessSessionException();
            return response.GetProperty("state").Clone();
        }
        try { return await RequestAsync(HttpMethod.Post, "access/command", data); }
        finally { if (command == "auth.logout") token = null; }
    }
    internal bool IsLocalServer => trust.Server.IsLoopback;

    private sealed record ResumeTicket(string Token, string Server, DateTimeOffset Expires);
    private static readonly TimeSpan ResumeLifetime = TimeSpan.FromMinutes(15);
    private static string ResumePath(string directory) => Path.Combine(directory, "resume-session.dat");

    /// <summary>
    /// Guarda la sesión actual para reabrirla tras una actualización. Se cifra con DPAPI (usuario actual),
    /// caduca a los 15 minutos y se elimina en cuanto se lee, así que solo sirve una vez.
    /// </summary>
    internal void SaveResume(string directory)
    {
        if (token is null) return;
        var clear = JsonSerializer.SerializeToUtf8Bytes(new ResumeTicket(token, trust.Server.AbsoluteUri, DateTimeOffset.UtcNow.Add(ResumeLifetime)));
        try
        {
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, $".resume-{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temporary, ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser));
                File.Move(temporary, ResumePath(directory), true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    internal static void DiscardResume(string directory)
    {
        try { File.Delete(ResumePath(directory)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    internal static bool HasResume(string directory) => File.Exists(ResumePath(directory));

    /// <summary>
    /// Recupera la sesión guardada antes de la actualización. Espera a que el servicio de la farmacia vuelva a estar
    /// disponible (puede estar reiniciándose) durante un máximo de 90 segundos.
    /// </summary>
    internal async Task<bool> TryResumeAsync(string directory, CancellationToken ct)
    {
        var path = ResumePath(directory);
        if (!File.Exists(path)) return false;
        ResumeTicket? saved;
        byte[]? clear = null;
        try
        {
            var encrypted = File.ReadAllBytes(path);
            File.Delete(path);
            clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            saved = JsonSerializer.Deserialize<ResumeTicket>(clear);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            DiscardResume(directory);
            return false;
        }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); }
        if (saved is null || saved.Expires <= DateTimeOffset.UtcNow || saved.Expires > DateTimeOffset.UtcNow.Add(ResumeLifetime)
            || saved.Server != trust.Server.AbsoluteUri || saved.Token.Length is < 16 or > 128) return false;
        token = saved.Token;
        var deadline = DateTimeOffset.UtcNow.AddSeconds(90);
        while (true)
        {
            try
            {
                await RequireUserAsync(allowFirstPassword: true);
                return true;
            }
            catch (Exception error) when (error is AccessSessionException or AccessDeniedException or InvalidDataException or JsonException)
            { token = null; return false; }
            catch (Exception error) when (error is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow >= deadline) { token = null; return false; }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }
    internal Task<JsonElement> OracleAsync(string action, object? payload = null)
    {
        if (action is not ("state" or "configure" or "forget" or "test")) throw new ArgumentException();
        return RequestAsync(HttpMethod.Post, "access/oracle/" + action, JsonSerializer.SerializeToElement(payload ?? new { }));
    }
    internal Task<JsonElement> UtilityAsync(string command, JsonElement? payload = null)
    {
        var path = command switch { "pharmacy.state" => "access/pharmacy/state", "pharmacy.save" => "access/pharmacy/save", "tenencia.state" => "access/tenencia/state", "tenencia.query" => "access/tenencia/query", "tenencia.schema" => "access/tenencia/schema", _ => throw new AccessDeniedException() };
        return RequestAsync(HttpMethod.Post, path, payload ?? JsonSerializer.SerializeToElement(new { }));
    }
    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, JsonElement? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body.Value);
        using var timeout = new CancellationTokenSource((path == "access/oracle/test" || path.StartsWith("access/tenencia/", StringComparison.Ordinal)) ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(20));
        using var response = await trust.Client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new AccessDeniedException("Demasiados intentos. Espera un minuto antes de volver a intentarlo.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) { token = null; throw new AccessSessionException(); }
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (text.Length > 16_777_216) throw new InvalidDataException();
        using var document = JsonDocument.Parse(text);
        if (!response.IsSuccessStatusCode)
        {
            var message = document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String ? error.GetString() : null;
            throw new AccessDeniedException(message ?? "No se pudo completar la operación de acceso.");
        }
        return document.RootElement.Clone();
    }
    public void Dispose() => trust.Dispose();
}
