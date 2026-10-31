using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using Viteka.Core;

namespace Viteka.Server;

[SupportedOSPlatform("windows")]
internal static class AccessServiceManagement
{
    private static readonly string[] Trusted = ["S-1-5-18", "S-1-5-32-544", "S-1-5-19"];
    private static string StateRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Viteka", ProductProfile.StateFolder);
    internal static async Task RunAsync(string action, string? clientDirectory)
    {
        var existing = ServiceController.GetServices().FirstOrDefault(s => s.ServiceName == AccessHost.ServiceName);
        using (existing)
        {
            if (action is "stop" or "uninstall" or "install")
            {
                if (existing is not null && existing.Status != ServiceControllerStatus.Stopped)
                { existing.Stop(); existing.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30)); }
            }
            if (action == "stop") return;
            if (action == "start") { if (existing?.Status == ServiceControllerStatus.Stopped) existing.Start(); return; }
            if (action == "uninstall")
            {
                if (existing is not null) Sc("delete", AccessHost.ServiceName);
                Firewall(false); return;
            }
            if (action != "install" || string.IsNullOrEmpty(clientDirectory)) throw new ArgumentException("Acción de servicio no válida.");
            Console.WriteLine("Preparando permisos del servicio.");
            ProtectDirectories();
            var server = Path.Combine(AppContext.BaseDirectory, "Viteka.Server.exe");
            var permissions = new DirectoryInfo(AppContext.BaseDirectory).GetAccessControl();
            permissions.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-19"), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(AppContext.BaseDirectory).SetAccessControl(permissions);
            var binary = "\"" + server + "\" --access-service";
            Console.WriteLine("Registrando servicio.");
            if (existing is null) Sc("create", AccessHost.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService", "DisplayName=", ProductProfile.IsReview?"Viteka Utilidades - Pruebas (solo este PC)":"Viteka Utilidades - Acceso de usuarios");
            else Sc("config", AccessHost.ServiceName, "binPath=", binary, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService");
            Sc("failure", AccessHost.ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000");
            using var service = new ServiceController(AccessHost.ServiceName);
            Console.WriteLine("Iniciando servicio.");
            service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(15));
            var bootstrap = Path.Combine(StateRoot, "Public", "access-server.json");
            var ready = false; string? lastProblem = null;
            for (var attempt = 0; attempt < 15; attempt++)
            {
                AccessServiceTrust? trust = null;
                try
                {
                    Console.WriteLine("Comprobando identidad HTTPS, intento " + (attempt + 1));
                    trust = new AccessServiceTrust(bootstrap); trust.Client.Timeout = TimeSpan.FromSeconds(2);
                    using var response = await trust.Client.GetAsync("access/health");
                    if (response.IsSuccessStatusCode) { ready = true; break; }
                    lastProblem = "HTTP " + (int)response.StatusCode;
                }
                catch (Exception error) when (error is IOException or System.Security.Cryptography.CryptographicException or System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException) { lastProblem = error.ToString() + "; TLS=" + trust?.ValidationFailure; }
                finally { trust?.Dispose(); }
                await Task.Delay(1000);
            }
            if (!ready) throw new InvalidOperationException("El servicio de usuarios no respondió con su identidad TLS. Las cuentas se han conservado. " + lastProblem);
            Directory.CreateDirectory(clientDirectory);
            foreach (var file in new[] { "access-server.json", "access-root.cer" }) File.Copy(Path.Combine(StateRoot, "Public", file), Path.Combine(clientDirectory, file), true);
            Firewall(true);
            Console.WriteLine("Servicio de usuarios listo. Las cuentas, credenciales y claves permanecen protegidas en el servidor.");
        }
    }
    private static void ProtectDirectories()
    {
        var privateDirectory = Path.Combine(StateRoot, "Private");
        foreach (var file in new[] { "access.dat", "access.initialized", "root-key.dat", "server-key.dat", "oracle.dat" })
        {
            var path = Path.Combine(privateDirectory, file); if (!File.Exists(path)) continue;
            var acl = new FileInfo(path).GetAccessControl();
            if (acl.GetOwner(typeof(SecurityIdentifier)) is not { } owner || !Trusted.Contains(owner.Value)) throw new InvalidDataException("El propietario del registro protegido no es el esperado. Se ha conservado para revisión.");
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow && !Trusted.Contains(rule.IdentityReference.Value) &&
                    (rule.FileSystemRights & (FileSystemRights.Write | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership)) != 0)
                    throw new InvalidDataException("El registro admite escritura no esperada. Se ha conservado para revisión.");
        }
        foreach (var path in new[] { StateRoot, privateDirectory, Path.Combine(StateRoot, "Public") })
        {
            Directory.CreateDirectory(path);
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.SetOwner(new SecurityIdentifier("S-1-5-32-544"));
            foreach (var sid in Trusted)
                security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid), sid == "S-1-5-19" ? FileSystemRights.Modify : FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            if (Path.GetFileName(path) == "Public") security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(path).SetAccessControl(security);
        }
    }
    private static void Sc(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException();
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000)) { process.Kill(); throw new System.TimeoutException("El control del servicio no respondió en 15 segundos."); }
        if (process.ExitCode != 0) throw new InvalidOperationException("No se pudo registrar o controlar el servicio de usuarios. " + output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
    }
    private static void Firewall(bool enabled)
    {
        var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2") ?? throw new InvalidOperationException();
        dynamic policy = Activator.CreateInstance(policyType) ?? throw new InvalidOperationException();
        object? created = null;
        try
        {
            try { policy.Rules.Remove(ProductProfile.ServiceName+"-LAN"); } catch (COMException) { }
            if (!enabled || ProductProfile.IsReview) return;
            var ruleType = Type.GetTypeFromProgID("HNetCfg.FWRule") ?? throw new InvalidOperationException();
            dynamic rule = Activator.CreateInstance(ruleType) ?? throw new InvalidOperationException(); created = rule;
            rule.Name = ProductProfile.ServiceName+"-LAN"; rule.Description = "Usuarios de Viteka por HTTPS en la red local de la farmacia.";
            rule.Protocol = 6; rule.LocalPorts = ProductProfile.AccessPort.ToString(); rule.Direction = 1; rule.Action = 1; rule.Profiles = 3;
            rule.RemoteAddresses = "LocalSubnet"; rule.Enabled = true; policy.Rules.Add(rule);
        }
        finally { if (created is not null) Marshal.FinalReleaseComObject(created); Marshal.FinalReleaseComObject((object)policy); }
    }
}
