using System.Text.Json;
using Viteka.Contracts;
using Viteka.Core;

namespace Viteka.Core;

public sealed class AccessSessionAgent(AccessDirectory authority)
{
    private AccessTicket? ticket;
    private DateTimeOffset expires;
    private int operation;
    private readonly Queue<DateTimeOffset> loginAttempts = new();
    public AccessUserView RequireUser(bool allowFirstPassword = false)
    {
        if (DateTimeOffset.UtcNow >= expires) ticket = null;
        var user = authority.Current(ticket);
        if (user.MustChangePassword && !allowFirstPassword) throw new AccessDeniedException("Cambia la contraseña inicial antes de continuar.");
        return user;
    }
    public object State()
    {
        try { return new { user = Public(RequireUser(true)) }; }
        catch (AccessSessionException) { ticket = null; return new { user = (object?)null }; }
    }
    private static object Public(AccessUserView user) => new { id = user.Id, name = user.Name, role = user.Role.ToString(), active = user.Active,
        mustChangePassword = user.MustChangePassword, bootstrapPassword = user.BootstrapPassword, utilityIds = user.UtilityIds, canConfigure = AccessPolicy.CanConfigure(user),
        canManageUsers = AccessPolicy.CanManageUsers(user), canInstallUpdates = AccessPolicy.CanInstallUpdates(user) };
    public async Task<object> HandleAsync(string command, JsonElement root)
    {
        if (command == "auth.state") return State();
        if (command == "auth.logout") { ticket = null; return State(); }
        if (Interlocked.CompareExchange(ref operation, 1, 0) != 0) throw new AccessDeniedException("Espera a que termine la operación de acceso.");
        try
        {
            var payload = root.TryGetProperty("payload", out var data) ? data : default;
            if (command == "auth.login")
            {
                var now = DateTimeOffset.UtcNow;
                while (loginAttempts.TryPeek(out var old) && now - old >= TimeSpan.FromMinutes(1)) loginAttempts.Dequeue();
                if (loginAttempts.Count >= 5) throw new AccessDeniedException("Espera un minuto antes de volver a intentarlo.");
                loginAttempts.Enqueue(now); ticket = null;
                var name = payload.GetProperty("name").GetString() ?? "";
                var password = payload.GetProperty("password").GetString() ?? "";
                var result = await Task.Run(() => authority.Login(name, password));
                ticket = result.Ticket; expires = now.AddHours(8); return State();
            }
            var actor = RequireUser(command == "auth.password");
            var current = ticket ?? throw new AccessSessionException();
            switch (command)
            {
                case "auth.password":
                    var oldPassword = payload.GetProperty("currentPassword").GetString() ?? "";
                    var newPassword = payload.GetProperty("newPassword").GetString() ?? "";
                    ticket = await Task.Run(() => authority.ChangeOwnPassword(current, oldPassword, newPassword));
                    return State();
                case "users.list":
                    return new { remoteAccessReady = authority.RemoteAccessReady(), users = authority.List(current).Select(Public), utilities = UtilityCatalog.Items.Select(u => new { id = u.Id, name = u.Name }) };
                case "users.create":
                    var created = await Task.Run(() => authority.Create(current, Text(payload, "name"), Role(payload), Text(payload, "password"), Ids(payload)));
                    return new { user = Public(created) };
                case "users.modify":
                    ticket = authority.Modify(current, Text(payload, "id"), Text(payload, "name"), Role(payload), payload.GetProperty("active").GetBoolean(), Ids(payload));
                    return State();
                case "users.password":
                    await Task.Run(() => authority.ResetPassword(current, Text(payload, "id"), Text(payload, "password")));
                    return new { changed = true };
                default: throw new AccessDeniedException();
            }
        }
        finally { Interlocked.Exchange(ref operation, 0); }
    }
    public object Utilities()
    {
        var actor = RequireUser();
        return new { utilities = UtilityCatalog.Items.Where(u => AccessPolicy.CanUse(actor, u.Id, UtilityCatalog.Items)).Select(u => new { id = u.Id, name = u.Name, description = u.Description }) };
    }
    private static string Text(JsonElement data, string key) => data.GetProperty(key).GetString() ?? "";
    private static string[] Ids(JsonElement data) => data.GetProperty("utilityIds").EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
    private static AccessRole Role(JsonElement data) => Enum.TryParse<AccessRole>(Text(data, "role"), out var value) && Enum.IsDefined(value) ? value : throw new ArgumentException("Rol no válido.");
}
