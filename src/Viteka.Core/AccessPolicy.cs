using Viteka.Contracts;

namespace Viteka.Core;

public enum AccessRole { User, Administrator, SuperAdministrator }
public sealed record AccessUserView(string Id, string Name, AccessRole Role, bool Active, bool MustChangePassword, string[] UtilityIds, bool BootstrapPassword = false);
public static class AccessPolicy
{
    public static bool CanConfigure(AccessUserView user) => user.Active && !user.MustChangePassword && user.Role == AccessRole.SuperAdministrator;
    public static bool CanManageUsers(AccessUserView user) => user.Active && !user.MustChangePassword && user.Role is AccessRole.Administrator or AccessRole.SuperAdministrator;
    public static bool CanInstallUpdates(AccessUserView user) => CanManageUsers(user);
    public static bool CanManage(AccessUserView actor, AccessUserView target) => CanManageUsers(actor) &&
        (actor.Role == AccessRole.SuperAdministrator || target.Role == AccessRole.User);
    public static bool CanCreateRole(AccessUserView actor, AccessRole requested) => CanManageUsers(actor) && Enum.IsDefined(requested) &&
        (actor.Role == AccessRole.SuperAdministrator || requested == AccessRole.User);
    public static bool CanUse(AccessUserView user, string id, IReadOnlyList<UtilityDefinition> catalog) =>
        user.Active && !user.MustChangePassword && catalog.Any(item => item.Id == id) &&
        (user.Role is AccessRole.Administrator or AccessRole.SuperAdministrator || user.UtilityIds.Contains(id, StringComparer.Ordinal));
    public static bool CanOpenPage(AccessUserView user, string page) => user.Active && !user.MustChangePassword && page switch
    {
        "Inicio" or "Aplicaciones" or "Actualizaciones" or "Mi cuenta" => true,
        "Favoritos" or "Actividad" or "Auditoría" => user.Role is AccessRole.Administrator or AccessRole.SuperAdministrator,
        "Usuarios" => CanManageUsers(user),
        "Configuración" => true,
        _ => false
    };
}
