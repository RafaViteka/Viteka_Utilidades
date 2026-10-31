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
    private sealed record DeclarationDraft(string ListVersion, Dictionary<string,string[]> Selections);
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
        var catalogs = new TenenciaCatalogStore(privateRoot, Path.Combine(AppContext.BaseDirectory, "Catalogs", "tenencia"));
        var database = catalogs.Database;
        database.Prune();
        var distribution=new CatalogDistribution(privateRoot,catalogs);
        var sessions = new ConcurrentDictionary<string, Session>();
        // Las sesiones sobreviven a un reinicio del servicio (p. ej. al actualizar): solo se guarda el hash del token.
        var sessionStore = new AccessSessionStore(privateRoot, Protect, Unprotect);
        foreach (var saved in sessionStore.Load())
            sessions[saved.TokenHash] = new(AccessSessionAgent.Restore(authority, new(saved.UserId, saved.PasswordVersion), saved.Expires), saved.Expires);
        void PersistSessions()
        {
            try
            {
                sessionStore.Save(sessions
                    .Where(item => item.Value.Agent.Ticket is not null && item.Value.Expires > DateTimeOffset.UtcNow)
                    .Select(item => new PersistedAccessSession(item.Key, item.Value.Agent.Ticket!.UserId, item.Value.Agent.Ticket.PasswordVersion, item.Value.Expires)));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // Sin persistencia, las sesiones siguen funcionando en memoria hasta el próximo reinicio.
            }
        }
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
            if(context.Request.Path.StartsWithSegments("/access/catalog-data")){
                var limit=context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();if(limit is not null&&!limit.IsReadOnly)limit.MaxRequestBodySize=6*1024*1024;
            }
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
            catch (Exception error) when (error is IOException or CryptographicException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "El registro protegido de usuarios no está disponible. Contacta con Viteka; no se han restablecido las cuentas." }); }
        });
        app.Use(async (context,next) => {
            var command="";
            if(context.Request.Path=="/access/command"){
                context.Request.EnableBuffering();
                try { var doc=await JsonDocument.ParseAsync(context.Request.Body); command=doc.RootElement.GetProperty("command").GetString()??""; doc.Dispose(); }
                catch(JsonException){} finally{context.Request.Body.Position=0;}
            }
            var path=context.Request.Path.Value??"";
            var action=path=="/access/login"?"auth.login":path=="/access/command"?command:path switch{
                "/access/tenencia/query"=>"tenencia.query", "/access/tenencia/state"=>"utility.open",
                "/access/pharmacy/save"=>"pharmacy.save", "/access/oracle/configure"=>"oracle.configure",
                "/access/oracle/forget"=>"oracle.forget", "/access/oracle/test"=>"oracle.test",
                "/access/tenencia-catalog/search"=>"catalog.search", _=>""};
            if(action is not ("auth.login" or "auth.logout" or "auth.password" or "users.create" or "users.modify" or "users.password" or "tenencia.query" or "utility.open" or "pharmacy.save" or "oracle.configure" or "oracle.forget" or "oracle.test" or "catalog.search"))action="";
            var operation=Guid.NewGuid().ToString("N");
            var unknown=new AuditActor("unknown","Acceso no identificado");
            if(action!=""){
                if(action!="auth.login"){try{_=Agent(context);}catch(AccessSessionException){}}
                database.Record(context.Items["audit-actor"] as AuditActor??unknown,action,"pending","Operación iniciada; resultado aún no confirmado.",operation,action.StartsWith("tenencia")||action=="utility.open"||action=="catalog.search"?UtilityCatalog.TenenciaId:"");
            }
            try { await next(); }
            catch {
                if(action!="")database.Complete(operation,context.Items["audit-actor"] as AuditActor??unknown,"error","La operación no se completó; no se guardan credenciales ni mensajes brutos.");
                throw;
            }
            if(action!="")database.Complete(operation,context.Items["audit-actor"] as AuditActor??unknown,context.Items["audit-result"] as string??(context.Response.StatusCode<400?"ok":"error"),context.Items["audit-note"] as string??"");
        });
        app.UseRateLimiter();
        AccessSessionAgent Agent(HttpContext context)
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Length > 256) throw new AccessSessionException();
            var key = Hash(authorization[7..]);
            if (!sessions.TryGetValue(key, out var session) || session.Expires <= DateTimeOffset.UtcNow) { sessions.TryRemove(key, out _); throw new AccessSessionException(); }
            var who = session.Agent.RequireUser(true); context.Items["audit-actor"] = new AuditActor(who.Id,who.Name); return session.Agent;
        }
        app.MapGet("/access/health", () => Results.Ok(new { status = "ok", version = typeof(AccessHost).Assembly.GetName().Version?.ToString(3) }));
        app.MapPost("/access/login", async (HttpContext context, JsonElement request) =>
        {
            foreach (var item in sessions.Where(item => item.Value.Expires <= DateTimeOffset.UtcNow)) sessions.TryRemove(item.Key, out _);
            if (sessions.Count >= 2000) return Results.StatusCode(429);
            var agent = new AccessSessionAgent(authority);
            var state = await agent.HandleAsync("auth.login", request);
            var logged = agent.RequireUser(true); context.Items["audit-actor"] = new AuditActor(logged.Id,logged.Name);
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            sessions[Hash(token)] = new(agent, agent.Expires);
            PersistSessions();
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
            if(command is "users.modify" or "users.password")context.Items["audit-note"]="Cuenta: "+request.GetProperty("id").GetString()+(command=="users.modify"?" · permisos: "+request.GetProperty("utilityIds").GetArrayLength():"");
            if (command == "auth.logout") sessions.TryRemove(Hash(context.Request.Headers.Authorization.ToString()[7..]), out _);
            if (command is "auth.password" or "auth.logout" or "users.modify") PersistSessions();
            return Results.Ok(result);
        });
        app.MapPost("/access/oracle/{action}", async (string action, HttpContext context, JsonElement request) =>
        {
            var actor = Agent(context).RequireUser();
            if (!AccessPolicy.CanConfigure(actor)) throw new AccessDeniedException();
            var response=await oracle.HandleAsync(action, request, context.RequestAborted);
            if(action=="test" && JsonSerializer.SerializeToElement(response).TryGetProperty("success",out var ok)&&!ok.GetBoolean())context.Items["audit-result"]="error";
            return Results.Ok(response);
        });
        app.MapPost("/access/pharmacy/{action}", (string action, HttpContext context, JsonElement request) =>
        {
            var actor = Agent(context).RequireUser(); if (!AccessPolicy.CanManageUsers(actor)) throw new AccessDeniedException();
            if (action == "save")
            {
                var profile = request.Deserialize<PharmacyProfile>(new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new ArgumentException("Datos no válidos."); pharmacy.Save(profile);
            }
            else if (action != "state") throw new AccessDeniedException();
            return Results.Ok(new { profile = pharmacy.Load(), provinces = SpanishProvinces.Items.Select(p => new { code = p.Key, name = p.Value }).OrderBy(p => p.name) });
        });
        app.MapPost("/access/tenencia/{action}", async (string action, HttpContext context, JsonElement request) =>
        {
            var actor = Agent(context).RequireUser();
            if (request.ValueKind != JsonValueKind.Object || request.EnumerateObject().Any()) throw new ArgumentException("La consulta utiliza exclusivamente la provincia y lista del servidor.");
            if (action == "schema")
            {
                if (!AccessPolicy.CanConfigure(actor)) throw new AccessDeniedException();
                return Results.Ok(await oracle.TenenciaAsync("", null, true, context.RequestAborted));
            }
            if (!AccessPolicy.CanUse(actor, UtilityCatalog.TenenciaId, UtilityCatalog.Items)) throw new AccessDeniedException();
            if (action is not ("state" or "query")) throw new AccessDeniedException();
            var profile = pharmacy.Load(); if (profile is null) return Results.Ok(new TenenciaResponse("pharmacy_pending", "El titular o Viteka deben guardar la provincia en Datos de la farmacia.", ""));
            TenenciaList? list;
            try { list = catalogs.Load(profile.Province); }
            catch (Exception error) when (error is InvalidDataException or JsonException) { return Results.Ok(new TenenciaResponse("catalog_invalid", "La lista provincial no es válida; Viteka debe revisarla antes de consultar.", profile.Province)); }
            if (list is null) return Results.Ok(new TenenciaResponse("catalog_pending", "Lista de tenencia pendiente para " + SpanishProvinces.Items[profile.Province] + ". No se ha evaluado el cumplimiento.", profile.Province) { ProvinceName = SpanishProvinces.Items[profile.Province] });
            var customized = catalogs.IsCustomized(profile.Province);
            if (action == "state")
            {
                // La última consulta se conserva en el servidor y la ven todos los puestos hasta la siguiente.
                var last = catalogs.LoadLast(profile.Province, list.Version);
                if (last is not null) return Results.Ok(last with { Message = "Última consulta guardada. Pulsa Consultar existencias para actualizarla.", Cached = true, Customized = customized, ProvinceName = SpanishProvinces.Items[profile.Province] });
                return Results.Ok(new TenenciaResponse("ready", "Lista disponible. Pulsa Consultar para leer las existencias actuales.", profile.Province, list.Version, list.Source, Rows: TenenciaRules.Preview(list)) { ProvinceName = SpanishProvinces.Items[profile.Province], Customized = customized });
            }
            var response = await oracle.TenenciaAsync(profile.Province, list, false, context.RequestAborted);
            if (!AccessPolicy.CanUse(Agent(context).RequireUser(), UtilityCatalog.TenenciaId, UtilityCatalog.Items)) throw new AccessDeniedException();
            var final = response with { ProvinceName = SpanishProvinces.Items[profile.Province], ListVersion = list.Version, Source = list.Source, Rows = response.Code == "ok" ? response.Rows : TenenciaRules.Preview(list), Customized = customized };
            catalogs.SaveLast(profile.Province, final);
            context.Items["audit-result"]=final.Code=="ok"?"ok":"error";
            context.Items["audit-note"]=final.Code=="ok"?final.Rows!.Length+" grupos · "+final.ListVersion:"Diagnóstico: "+final.Code;
            return Results.Ok(final);
        });
        // Edición del listado y búsqueda de artículos: exclusivamente Viteka (superadministrador).
        app.MapPost("/access/tenencia-catalog/{action}", async (string action, HttpContext context, JsonElement request) =>
        {
            var actor = Agent(context).RequireUser();
            if (!AccessPolicy.CanConfigure(actor)) throw new AccessDeniedException();
            var profile = pharmacy.Load() ?? throw new ArgumentException("Guarda primero la provincia en Datos de la farmacia.");
            if (action == "search")
            {
                string Value(string name) => request.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                var text = Value("text"); if (text.Length > 12000) throw new ArgumentException("Búsqueda demasiado larga.");
                return Results.Ok(await oracle.SearchArticlesAsync(Value("mode"), text, Value("field") is { Length: > 0 } f ? f : null, context.RequestAborted));
            }
            TenenciaList? list;
            if (action == "state") list = catalogs.Load(profile.Province);
            else if (action is "group.save" or "group.delete" or "codes.add" or "codes.remove" or "reset") list = catalogs.Apply(profile.Province, action, request, new(actor.Id,actor.Name));
            else throw new AccessDeniedException();
            return Results.Ok(new { list, customized = catalogs.IsCustomized(profile.Province), provinceName = SpanishProvinces.Items[profile.Province], conflicts=catalogs.Revisions.Read(profile.Province)?.Conflicts, masterVersion=catalogs.Revisions.Read(profile.Province)?.Base.Version });
        });
        app.MapPost("/access/catalog-data/{action}",async(string action,HttpContext context,JsonElement request)=>{
            var user=Agent(context).RequireUser();if(!AccessPolicy.CanConfigure(user))throw new AccessDeniedException();
            var profile=pharmacy.Load()??throw new ArgumentException("Configura primero la provincia.");var province=profile.Province;
            _=catalogs.Load(province);var actor=new AuditActor(user.Id,user.Name);
            string Text(string name)=>request.GetProperty(name).GetString()??"";
            return action switch{
                "state"=>Results.Ok(distribution.State(province)),
                "check"=>Results.Ok(await distribution.Check(province,context.RequestAborted)),
                "prepare"=>Results.Ok(new{package=distribution.Prepare(province,Text("version"),actor)}),
                "preview"=>Results.Ok(distribution.Preview(province,request.GetProperty("package").Deserialize<SignedCatalog>(PharmacyDatabase.Json)??throw new ArgumentException())),
                "apply"=>Results.Ok(distribution.Apply(province,request.GetProperty("package").Deserialize<SignedCatalog>(PharmacyDatabase.Json)??throw new ArgumentException(),Text("expectedVersion"),actor)),
                "rollback"=>Results.Ok(catalogs.Revisions.Rollback(province,request.GetProperty("revision").GetInt64(),Text("expectedVersion"),actor)),
                "resolve"=>Results.Ok(catalogs.Revisions.Resolve(province,Text("groupId"),Text("field"),request.GetProperty("useMaster").GetBoolean(),Text("expectedVersion"),actor)),
                _=>throw new AccessDeniedException()
            };
        });
        app.MapPost("/access/audit/settings",(HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();if(!AccessPolicy.CanManageUsers(actor))throw new AccessDeniedException();
            if(request.TryGetProperty("months",out var months)){
                if(!AccessPolicy.CanConfigure(actor))throw new AccessDeniedException();database.SetRetention(months.GetInt32(),new(actor.Id,actor.Name));
            }
            return Results.Ok(new{months=database.RetentionMonths()});
        });
        app.MapPost("/access/audit/query",(HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();if(!AccessPolicy.CanManageUsers(actor))throw new AccessDeniedException();
            database.Prune();
            var filter=request.Deserialize<AuditFilter>(PharmacyDatabase.Json)??new();var rows=database.Query(filter);
            return Results.Ok(new{rows=rows.Take(50),hasMore=rows.Length>50,next=rows.Length>50?rows[49].Id:(long?)null});
        });
        app.MapPost("/access/activity",(HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();var action=request.GetProperty("action").GetString()??"";
            if(action is not ("export.xlsx.saved" or "export.csv.saved" or "export.pdf.saved" or "print.requested" or "update.started" or "update.failed" or "update.running"))throw new AccessDeniedException();
            if(action.StartsWith("update.")&&!AccessPolicy.CanInstallUpdates(actor)||!action.StartsWith("update.")&&!AccessPolicy.CanUse(actor,UtilityCatalog.TenenciaId,UtilityCatalog.Items))throw new AccessDeniedException();
            var operation=request.GetProperty("operationId").GetString();if(!Guid.TryParse(operation,out _))throw new ArgumentException();
            var note=action=="print.requested"?"Solicitud de impresión; no acredita impresión física.":"Evento confirmado por el cliente autorizado.";
            if(action.StartsWith("update.")&&request.TryGetProperty("version",out var offered)&&offered.ValueKind==JsonValueKind.String){if(!Version.TryParse(offered.GetString(),out var version))throw new ArgumentException();note="Versión "+version.ToString(3)+" · "+note;}
            database.Record(new(actor.Id,actor.Name),action,action.EndsWith("failed")?"error":"ok",note,operation,action.StartsWith("update")?"":UtilityCatalog.TenenciaId);return Results.Ok(new{saved=true});
        });
        app.MapPost("/access/declaration/{action}",(string action,HttpContext context,JsonElement request)=>{
            var actor=Agent(context).RequireUser();if(!AccessPolicy.CanUse(actor,UtilityCatalog.TenenciaId,UtilityCatalog.Items))throw new AccessDeniedException();
            var profile=pharmacy.Load()??throw new ArgumentException("Configura la farmacia.");var list=catalogs.Load(profile.Province)??throw new ArgumentException();
            var key="declaration."+profile.Province+"."+actor.Id;
            return database.Transaction((c,tx)=>{
                var saved=PharmacyDatabase.Get(c,tx,key);
                var selections=saved is null?new Dictionary<string,string[]>():JsonSerializer.Deserialize<DeclarationDraft>(saved,PharmacyDatabase.Json)!.Selections;
                var clean=selections.Where(x=>list.Groups.Any(g=>g.Id==x.Key)).ToDictionary(x=>x.Key,x=>x.Value.Where(cn=>list.Groups.First(g=>g.Id==x.Key).Codes.Contains(cn)).Distinct().ToArray());
                if(action=="save"){
                    var draft=request.Deserialize<DeclarationDraft>(PharmacyDatabase.Json)??throw new ArgumentException();
                    if(draft.ListVersion!=list.Version)throw new ArgumentException("El listado cambió; revisa la selección.");
                    foreach(var item in draft.Selections)if(!list.Groups.Any(g=>g.Id==item.Key&&item.Value.All(cn=>g.Codes.Contains(cn))))throw new ArgumentException("Artículo ajeno al grupo.");
                    clean=draft.Selections;PharmacyDatabase.Set(c,tx,key,draft);
                    PharmacyDatabase.Record(c,tx,new(actor.Id,actor.Name),"declaration.save","ok",clean.Sum(x=>x.Value.Length)+" artículos seleccionados",utility:UtilityCatalog.TenenciaId);
                }else if(action!="state")throw new AccessDeniedException();
                return Results.Ok(new{listVersion=list.Version,groups=list.Groups,selections=clean,reviewRequired=saved is not null&&JsonSerializer.Deserialize<DeclarationDraft>(saved,PharmacyDatabase.Json)!.ListVersion!=list.Version});
            });
        });
        await app.RunAsync();
    }
    private static byte[] Protect(byte[] value) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return ProtectedData.Protect(value, null, DataProtectionScope.LocalMachine); }
    private static byte[] Unprotect(byte[] value) { if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException(); return ProtectedData.Unprotect(value, null, DataProtectionScope.LocalMachine); }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
