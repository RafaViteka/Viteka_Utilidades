using Viteka.Contracts;
using Viteka.Core;
using Viteka.Server;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Hosting.WindowsServices;

if (args.Length > 0 && args[0].StartsWith("--access-", StringComparison.Ordinal))
{
    try { await AccessHost.RunAsync(args); }
    catch (Exception error) when (error is not OutOfMemoryException)
    {
        // CLI failures must return a bounded error, not invoke an interactive Windows crash reporter.
        Console.Error.WriteLine("No se pudo completar la operación de acceso: " + error.GetType().Name + ": " + error.Message);
        Environment.ExitCode = 2;
    }
    return;
}

var serviceMode = args.Contains("--service") || (OperatingSystem.IsWindows() && WindowsServiceHelpers.IsWindowsService());
args = args.Where(x => x != "--service").ToArray();
if (serviceMode && !OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("El modo de servicio requiere Windows.");
    Environment.ExitCode = 2;
    return;
}
var stateDirectory = Environment.GetEnvironmentVariable("VITEKA_SERVER_STATE_DIRECTORY") ?? (serviceMode
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Viteka", "UtilidadesPreview", "Server")
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viteka", "Server"));
var registry = new DeviceRegistry(stateDirectory);
// Service administration must use --service and elevation to access the protected ProgramData registry.
if (args.Length > 0 && args[0].StartsWith("--admin-", StringComparison.Ordinal))
{
    if (args is ["--admin-issue-code", var output])
    {
        // FileMode.CreateNew prevents accidentally overwriting existing files; no secrets in stdout/logs.
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var file = new FileStream(Path.GetFullPath(output), options);
        using var writer = new StreamWriter(file);
        writer.Write(registry.IssueCode());
        Console.WriteLine("Código creado en el archivo indicado. Caduca en 5 minutos y solo admite un uso.");
    }
    else if (args is ["--admin-list-devices"])
        foreach (var device in registry.ListDevices()) Console.WriteLine($"{device.Id} | {device.Name} | {device.ExpiresAt:O} | Revocado={device.Revoked}");
    else if (args is ["--admin-revoke-device", var id])
        Environment.ExitCode = registry.Revoke(id) ? 0 : 2;
    else { Console.Error.WriteLine("Comando administrativo no válido."); Environment.ExitCode = 2; }
    return;
}

if (serviceMode)
{
    // Fail early if service permissions or its existing registry are unusable. Never reset it.
    Directory.CreateDirectory(stateDirectory);
    registry.ListDevices();
}
var builder = WebApplication.CreateBuilder(args);
if (serviceMode) builder.Services.AddWindowsService(options => options.ServiceName = "VitekaUtilidadesPreview");
// Development host only. LAN binding requires verified TLS deployment first.
builder.WebHost.UseUrls("http://127.0.0.1:5187");
builder.Services.AddSingleton(registry);
builder.Services.AddSingleton<IPharmacyReader, UnconfiguredPharmacyReader>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("pairing", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }
    context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null;
        if (!registry.IsAuthorized(token))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            return;
        }
    }
    await next();
});
app.MapGet("/health", () => Results.Ok(new { status = "ok", mode = "demo" }));
app.MapPost("/pairing/redeem", (PairingRequest request) =>
{
    var credential = registry.Redeem(request.Code, request.DeviceName);
    return credential is null ? Results.Unauthorized() : Results.Ok(new PairingResponse(credential.DeviceId, credential.Token, credential.ExpiresAt));
}).RequireRateLimiting("pairing");
app.MapGet("/api/status", () => new ServiceStatus("demo", "not_configured", false, typeof(ServiceStatus).Assembly.GetName().Version?.ToString(3) ?? "dev"));
app.MapGet("/api/utilities", () => DemoCatalog.Items);
app.MapPost("/api/utilities/{id}/execute", async (string id, IPharmacyReader pharmacy, CancellationToken ct) =>
{
    if (!DemoCatalog.Items.Any(x => x.Id == id)) return Results.NotFound();
    var result = await pharmacy.CheckAvailabilityAsync(ct);
    return Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
});
app.Run();
