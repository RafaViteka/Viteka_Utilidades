using System.Security.Cryptography;
using System.Text.Json;
using Viteka.Contracts;

namespace Viteka.Core;

public sealed class AccessDeniedException(string message = "No tienes permiso para esta acción.") : InvalidOperationException(message);
public sealed class AccessSessionException() : InvalidOperationException("Inicia sesión de nuevo.");
public sealed record AccessTicket(string UserId, string PasswordVersion);
public sealed record AccessLogin(AccessTicket Ticket, AccessUserView User);

// The host supplies a protected store and owns the session ticket. Public results
// contain user views only; password verifiers never leave this authority.
public sealed class AccessDirectory(string directory, IReadOnlyList<UtilityDefinition> catalog,
    Func<byte[], byte[]> protect, Func<byte[], byte[]> unprotect, TimeProvider? clock = null)
{
    private const int Iterations = 600_000;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private string FilePath => Path.Combine(directory, "access.dat");
    private string Marker => Path.Combine(directory, "access.initialized");
    private sealed class Document
    {
        public Document() { }
        public int Schema { get; set; } = 1;
        public List<Account> Users { get; set; } = [];
    }
    private sealed class Account
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public AccessRole Role { get; set; }
        public bool Active { get; set; } = true;
        public bool MustChange { get; set; } = true;
        public bool Bootstrap { get; set; }
        public string[] UtilityIds { get; set; } = [];
        public int Iterations { get; set; } = 600_000;
        public string Salt { get; set; } = "";
        public string Hash { get; set; } = "";
        public string PasswordVersion { get; set; } = Guid.NewGuid().ToString("N");
        public int Failures { get; set; }
        public DateTimeOffset? LockedUntil { get; set; }
        public Account() { }
    }
    public void InitializeDefaults()
    {
        using var guard = Lock();
        if (File.Exists(FilePath)) { _ = Read(); if (!File.Exists(Marker)) File.WriteAllText(Marker, "1"); return; }
        if (File.Exists(Marker)) throw new InvalidDataException("Falta el registro de acceso. No se restablecerán las cuentas iniciales.");
        var state = new Document();
        state.Users.Add(NewAccount("Viteka", AccessRole.SuperAdministrator, "jupiter", [], true));
        state.Users.Add(NewAccount("F. titular", AccessRole.Administrator, "1234", [], true));
        state.Users.Add(NewAccount("usuario", AccessRole.User, "0000", [], true));
        Write(state); File.WriteAllText(Marker, "1");
    }
    public bool RemoteAccessReady() { using var guard = Lock(); return !Read().Users.Any(u => u.Active && u.Bootstrap); }
    public AccessLogin Login(string name, string password)
    {
        using var guard = Lock(); var state = Read();
        var user = name is not null && name.Length <= 64 ? state.Users.FirstOrDefault(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) : null;
        var validInput = password is not null && password.Length <= 1024;
        if (user is null || !user.Active)
        {
            // Similar work for an unknown account; do not reveal whether a name exists.
            if (validInput) _ = Rfc2898DeriveBytes.Pbkdf2(password!, new byte[16], Iterations, HashAlgorithmName.SHA256, 32);
            throw new AccessDeniedException("Usuario o contraseña incorrectos, o cuenta temporalmente bloqueada.");
        }
        if (user.LockedUntil is {} until && until > time.GetUtcNow()) throw new AccessDeniedException("Usuario o contraseña incorrectos, o cuenta temporalmente bloqueada.");
        if (!validInput || !Verify(user, password!))
        {
            user.Failures++;
            if (user.Failures >= 5) { user.LockedUntil = time.GetUtcNow().AddMinutes(2); user.Failures = 0; }
            Write(state); throw new AccessDeniedException("Usuario o contraseña incorrectos, o cuenta temporalmente bloqueada.");
        }
        user.Failures = 0; user.LockedUntil = null; Write(state);
        return new(new(user.Id, user.PasswordVersion), View(user));
    }
    public AccessUserView Current(AccessTicket? ticket)
    { using var guard = Lock(); return View(Require(Read(), ticket, true)); }
    public AccessUserView[] List(AccessTicket ticket)
    {
        using var guard = Lock(); var state = Read(); var actor = Require(state, ticket);
        if (!AccessPolicy.CanManageUsers(View(actor))) throw new AccessDeniedException();
        return state.Users.OrderBy(u => u.Name, StringComparer.OrdinalIgnoreCase).Select(View).ToArray();
    }
    public AccessUserView Create(AccessTicket ticket, string name, AccessRole role, string password, string[] utilityIds)
    {
        ValidateName(name); ValidateNewPassword(password); ValidateUtilities(utilityIds);
        using var guard = Lock(); var state = Read(); var actor = Require(state, ticket);
        if (!AccessPolicy.CanCreateRole(View(actor), role)) throw new AccessDeniedException();
        if (state.Users.Count >= 200 || state.Users.Any(u => u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("El nombre ya existe o se alcanzó el límite de cuentas.");
        var user = NewAccount(name.Trim(), role, password, utilityIds); state.Users.Add(user); Write(state); return View(user);
    }
    public AccessTicket ChangeOwnPassword(AccessTicket ticket, string currentPassword, string newPassword)
    {
        ValidateNewPassword(newPassword);
        using var guard = Lock(); var state = Read(); var actor = Require(state, ticket, true);
        if (currentPassword is null || currentPassword.Length > 1024 || !Verify(actor, currentPassword)) throw new AccessDeniedException("La contraseña actual no es correcta.");
        if (Verify(actor, newPassword)) throw new ArgumentException("La nueva contraseña debe ser distinta.");
        SetPassword(actor, newPassword); actor.MustChange = false; Write(state); return new(actor.Id, actor.PasswordVersion);
    }
    public void ResetPassword(AccessTicket ticket, string id, string newPassword)
    {
        ValidateNewPassword(newPassword);
        using var guard = Lock(); var state = Read(); var actor = Require(state, ticket); var target = Find(state, id);
        if (target.Id == actor.Id) throw new AccessDeniedException("Para cambiar tu contraseña utiliza Mi cuenta y confirma la actual.");
        if (!AccessPolicy.CanManage(View(actor), View(target))) throw new AccessDeniedException();
        SetPassword(target, newPassword); target.MustChange = true; Write(state);
    }
    public AccessTicket Modify(AccessTicket ticket, string id, string name, AccessRole role, bool active, string[] utilityIds)
    {
        ValidateName(name); ValidateUtilities(utilityIds);
        using var guard = Lock(); var state = Read(); var actor = Require(state, ticket); var target = Find(state, id);
        if (!AccessPolicy.CanManage(View(actor), View(target)) || !AccessPolicy.CanCreateRole(View(actor), role)) throw new AccessDeniedException();
        if (state.Users.Any(u => u.Id != id && u.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Ese nombre ya existe.");
        if ((!active || role != AccessRole.SuperAdministrator) && target.Role == AccessRole.SuperAdministrator && target.Active && state.Users.Count(u => u.Active && u.Role == AccessRole.SuperAdministrator) <= 1)
            throw new ArgumentException("Debe quedar al menos un superadministrador activo.");
        target.Name = name.Trim(); target.Role = role; target.Active = active; target.UtilityIds = utilityIds.Distinct().ToArray();
        target.PasswordVersion = Guid.NewGuid().ToString("N"); Write(state);
        return target.Id == actor.Id && active ? new(target.Id, target.PasswordVersion) : ticket;
    }
    private static Account Find(Document state, string id) => state.Users.FirstOrDefault(u => u.Id == id) ?? throw new ArgumentException("Cuenta no encontrada.");
    private static Account Require(Document state, AccessTicket? ticket, bool allowPasswordChange = false)
    {
        var user = ticket is null ? null : state.Users.FirstOrDefault(u => u.Id == ticket.UserId && u.PasswordVersion == ticket.PasswordVersion && u.Active);
        if (user is null) throw new AccessSessionException();
        if (user.MustChange && !allowPasswordChange) throw new AccessDeniedException("Cambia la contraseña inicial antes de continuar.");
        return user;
    }
    private static AccessUserView View(Account user) => new(user.Id, user.Name, user.Role, user.Active, user.MustChange, user.UtilityIds.ToArray(), user.Bootstrap);
    private static Account NewAccount(string name, AccessRole role, string password, string[] ids, bool bootstrap = false)
    { var user = new Account { Name = name, Role = role, UtilityIds = ids.Distinct().ToArray() }; SetPassword(user, password); user.Bootstrap = bootstrap; return user; }
    private static void SetPassword(Account user, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        user.Iterations = Iterations;
        user.Salt = Convert.ToBase64String(salt);
        user.Hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32));
        user.PasswordVersion = Guid.NewGuid().ToString("N"); user.Failures = 0; user.LockedUntil = null; user.Bootstrap = false;
    }
    private static bool Verify(Account user, string password) => CryptographicOperations.FixedTimeEquals(
        Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(user.Salt), user.Iterations, HashAlgorithmName.SHA256, 32), Convert.FromBase64String(user.Hash));
    private static void ValidateName(string name)
    { if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64 || name.Any(char.IsControl)) throw new ArgumentException("Nombre de usuario no válido."); }
    private static void ValidateNewPassword(string password)
    { if (string.IsNullOrWhiteSpace(password) || password.Length < 3 || password.Length > 128) throw new ArgumentException("La contraseña nueva debe tener entre 3 y 128 caracteres."); }
    private void ValidateUtilities(string[] ids)
    { if (ids is null || ids.Length > 500 || ids.Any(id => !catalog.Any(item => item.Id == id))) throw new ArgumentException("Aplicación no disponible."); }
    private FileStream Lock()
    {
        Directory.CreateDirectory(directory);
        for (var attempt = 0; ; attempt++)
        { try { return new FileStream(Path.Combine(directory, "access.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
          catch (IOException) when (attempt < 50) { Thread.Sleep(40); } }
    }
    private Document Read()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 2_097_152) throw new InvalidDataException("Registro de acceso ausente o inválido.");
        var bytes = unprotect(File.ReadAllBytes(FilePath));
        try
        {
            var state = JsonSerializer.Deserialize<Document>(bytes) ?? throw new InvalidDataException();
            if (state.Schema != 1 || state.Users is null || state.Users.Count is < 1 or > 200 ||
                state.Users.Any(u => u is null || !Enum.IsDefined(u.Role) || string.IsNullOrWhiteSpace(u.Name) || u.Name.Length > 64 ||
                    !Guid.TryParseExact(u.Id, "N", out _) || !Guid.TryParseExact(u.PasswordVersion, "N", out _) || u.UtilityIds is null || u.UtilityIds.Length > 500 ||
                    u.Iterations is < 600_000 or > 2_000_000 || u.UtilityIds.Any(id => string.IsNullOrEmpty(id) || id.Length > 128) || string.IsNullOrEmpty(u.Salt) || string.IsNullOrEmpty(u.Hash) || Convert.FromBase64String(u.Salt).Length != 16 || Convert.FromBase64String(u.Hash).Length != 32) ||
                state.Users.Select(u => u.Id).Distinct().Count() != state.Users.Count || state.Users.Select(u => u.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != state.Users.Count ||
                !state.Users.Any(u => u.Active && u.Role == AccessRole.SuperAdministrator)) throw new InvalidDataException("Registro de acceso no válido.");
            return state;
        }
        catch (Exception error) when (error is FormatException or JsonException) { throw new InvalidDataException("Registro de acceso no válido.", error); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    private void Write(Document state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state); byte[] encrypted;
        try { encrypted = protect(bytes); } finally { CryptographicOperations.ZeroMemory(bytes); }
        var temporary = Path.Combine(directory, $".access-{Guid.NewGuid():N}.tmp");
        try { using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(encrypted); file.Flush(true); } File.Move(temporary, FilePath, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
