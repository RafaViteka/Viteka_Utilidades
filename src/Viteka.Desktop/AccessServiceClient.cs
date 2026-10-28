using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    internal Task<JsonElement> OracleAsync(string action, object? payload = null)
    {
        if (action is not ("state" or "configure" or "forget" or "test")) throw new ArgumentException();
        return RequestAsync(HttpMethod.Post, "access/oracle/" + action, JsonSerializer.SerializeToElement(payload ?? new { }));
    }
    private async Task<JsonElement> RequestAsync(HttpMethod method, string path, JsonElement? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body.Value);
        using var timeout = new CancellationTokenSource(path == "access/oracle/test" ? TimeSpan.FromSeconds(40) : TimeSpan.FromSeconds(20));
        using var response = await trust.Client.SendAsync(request, timeout.Token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests) throw new AccessDeniedException("Demasiados intentos. Espera un minuto antes de volver a intentarlo.");
        if (response.StatusCode == HttpStatusCode.Unauthorized) { token = null; throw new AccessSessionException(); }
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (text.Length > 1_048_576) throw new InvalidDataException();
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
