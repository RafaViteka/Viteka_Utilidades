using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Viteka.Contracts;
using Viteka.Core;

namespace Viteka.Server;

internal static class AccessHost
{
    internal const string ServiceName = "VitekaUtilidadesAccess";
    private sealed record Session(AccessSessionAgent Agent, DateTimeOffset Expires);
    internal static async Task RunAsync(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("El servicio de acceso requiere Windows."); Environment.ExitCode = 2; return; }
        if (args.Length is 2 or 3 && args[0] == "--access-manage")
        { await AccessServiceManagement.RunAsync(args[1], args.Length == 3 ? args[2] : null); return; }
        if (args is ["--access-health", var bootstrap])
        {
            using var trust = new AccessServiceTrust(bootstrap);
            trust.Client.Timeout = TimeSpan.FromSeconds(2);
            try
            {
                using var response = await trust.Client.GetAsync("access/health");
                response.EnsureSuccessStatusCode(); Console.WriteLine("Servicio de acceso disponible con identidad TLS verificada.");
            }
            catch (Exception error) when (error is System.Net.Http.HttpRequestException or OperationCanceledException)
            { throw new InvalidOperationException("No se verificó el servicio: " + error.Message + "; TLS=" + trust.ValidationFailure, error); }
            return;
        }
        var service = args is ["--access-service"];
        var stateRoot = service ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Viteka", "UtilidadesAccess")
            : args.Length is 2 or 3 && args[0] == "--access-console" ? Path.GetFullPath(args[1]) : throw new ArgumentException("Modo de acceso no válido.");
        var port = !service && args.Length == 3 ? int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 7447;
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        var privateRoot = Path.Combine(stateRoot, "Private"); var publicRoot = Path.Combine(stateRoot, "Public");
        using var certificate = AccessCertificates.LoadOrCreate(privateRoot, publicRoot, port);
        var authority = new AccessDirectory(privateRoot, UtilityCatalog.Items,
            Protect, Unprotect);
        authority.InitializeDefaults();
        var oracle = new OracleService(privateRoot);
        var pharmacy = new PharmacyDirectory(privateRoot, Protect, Unprotect);
        var sessions = new ConcurrentDictionary<string, Session>();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory, Args = [] });
        if (service) builder.Services.AddWindowsService(options => options.ServiceName = ServiceName);
        var protocols = SslProtocols.Tls12 | (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348) ? SslProtocols.Tls13 : SslProtocols.None);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 32768;
            if (service) options.ListenAnyIP(port, listen => listen.UseHttps(https => { https.ServerCertificate = certificate; https.SslProtocols = protocols; }));
            else options.ListenLocalhost(port, listen => listen.UseHttps(https => { https.ServerCertificate = certificate; https.SslProtocols = protocols; }));
        });
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("access-login", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
        });
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            try
            {
                var remote = context.Connection.RemoteIpAddress;
                if (remote is null || !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
                    if (!authority.RemoteAccessReady()) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = "Finaliza el cambio de las cuentas iniciales en el servidor antes de acceder desde los puestos." }); return; }
                await next();
            }
            catch (AccessSessionException) { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "AUTH_REQUIRED" }); }
            catch (AccessDeniedException error) { context.Response.StatusCode = 403; await context.Response.WriteAsJsonAsync(new { error = error.Message }); }
            catch (Exception error) when (error is ArgumentException or KeyNotFoundException or InvalidOperationException or JsonException)
            { context.Response.StatusCode = 400; await context.Response.WriteAsJsonAsync(new { error = error is ArgumentException ? error.Message : "Solicitud de acceso no válida." }); }
            catch (Exception error) when (error is IOException or CryptographicException or UnauthorizedAccessException)
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "El registro protegido de usuarios no está disponible. Contacta con Viteka; no se han restablecido las cuentas." }); }
        });
        app.UseRateLimiter();
        AccessSessionAgent Agent(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length > 256) throw new AccessSessionException();
            var key = Hash(authorization[7..]);
            if (!sessions.TryGetValue(key, out var session) || session.Expires <= DateTimeOffset.UtcNow) { sessions.TryRemove(key, out _); throw new AccessSessionException(); }
            _ = session.Agent.RequireUser(true); return session.Agent;
        }
        app.MapGet("/access/health", () => Results.Ok(new { status = "ok", version = typeof(AccessHost).Assembly.GetName().Version?.ToString(3) }));
        app.MapPost("/access/login", async (JsonElement request) =>
        {
            foreach (var item in sessions.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow)) sessions.TryRemove(item.Key, out _);
            if (sessions.Count >= 2000) return Results.StatusCode(429);
            var agent = new AccessSessionAgent(authority);
            var state = await agent.HandleAsync("auth.login", request);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[Hash(token)] = new(agent, DateTimeOffset.UtcNow.AddHours(8));
            return Results.Ok(new { token, state });
        }).RequireRateLimiting("access-login");
        app.MapGet("/access/me", (HttpContext context) => Results.Ok(Agent(context).State()));
        app.MapPost("/access/command", async (HttpContext context, JsonElement request) =>
        {
            var agent = Agent(context);
            var command = request.GetProperty("command").GetString();
            if (command == "utilities.list") return Results.Ok(agent.Utilities());
            if (command is not ("auth.password" or "auth.logout" or "users.list" or "users.create" or "users.modify" or "users.password")) throw new AccessDeniedException();
            var result = await agent.HandleAsync(command, request);
            if (command == "auth.logout") sessions.TryRemove(Hash(context.Request.Headers.Authorization.ToString()[7..]), out _);
            return Results.Ok(result);
        });
        app.MapPost("/access/oracle/{action}", async (string action, HttpContext context, JsonElement request) =>
        {
            var actor = Agent(context).RequireUser();
            if (!AccessPolicy.CanConfigure(actor)) throw new AccessDeniedException();
            return Results.Ok(await oracle.HandleAsync(action, request, context.RequestAborted));
        });
        app.MapPost("/access/pharmacy/{action}", (string action,HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();if(!AccessPolicy.CanManageUsers(actor))throw new AccessDeniedException();
            if(action=="save"){
                var profile=request.Deserialize<PharmacyProfile>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new ArgumentException("Datos no válidos.");pharmacy.Save(profile);
            }else if(action!="state")throw new AccessDeniedException();
            return Results.Ok(new{profile=pharmacy.Load(),provinces=SpanishProvinces.Items.Select(p=>new{code=p.Key,name=p.Value}).OrderBy(p=>p.name)});
        });
        app.MapPost("/access/tenencia/{action}", async (string action,HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();
            if(request.ValueKind!=JsonValueKind.Object||request.EnumerateObject().Any())throw new ArgumentException("La consulta utiliza exclusivamente la provincia y lista del servidor.");
            if(action=="schema"){
                if(!AccessPolicy.CanConfigure(actor))throw new AccessDeniedException();
                return Results.Ok(await oracle.TenenciaAsync("",null,true,context.RequestAborted));
            }
            if(!AccessPolicy.CanUse(actor,UtilityCatalog.TenenciaId,UtilityCatalog.Items))throw new AccessDeniedException();
            if(action is not ("state" or "query"))throw new AccessDeniedException();
            var profile=pharmacy.Load();if(profile is null)return Results.Ok(new TenenciaResponse("pharmacy_pending","El titular o Viteka deben guardar la provincia en Datos de la farmacia.",""));
            TenenciaList? list;
            try{list=TenenciaRules.Load(Path.Combine(AppContext.BaseDirectory,"Catalogs","tenencia"),profile.Province);}
            catch(Exception error) when(error is InvalidDataException or JsonException){return Results.Ok(new TenenciaResponse("catalog_invalid","La lista provincial no es válida; Viteka debe revisarla antes de consultar.",profile.Province));}
            if(list is null)return Results.Ok(new TenenciaResponse("catalog_pending","Lista de tenencia pendiente para "+SpanishProvinces.Items[profile.Province]+". No se ha evaluado el cumplimiento.",profile.Province));
            if(action=="state")return Results.Ok(new TenenciaResponse("ready","Lista disponible. Pulsa Consultar para leer las existencias actuales.",profile.Province,list.Version,list.Source){ProvinceName=SpanishProvinces.Items[profile.Province]});
            var response=await oracle.TenenciaAsync(profile.Province,list,false,context.RequestAborted);
            if(!AccessPolicy.CanUse(Agent(context).RequireUser(),UtilityCatalog.TenenciaId,UtilityCatalog.Items))throw new AccessDeniedException();
            return Results.Ok(response with{ProvinceName=SpanishProvinces.Items[profile.Province],ListVersion=list.Version,Source=list.Source});
        });
        await app.RunAsync();
    }
    private static byte[] Protect(byte[] value) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return ProtectedData.Protect(value, null, DataProtectionScope.LocalMachine); }
    private static byte[] Unprotect(byte[] value) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return ProtectedData.Unprotect(value, null, DataProtectionScope.LocalMachine); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
