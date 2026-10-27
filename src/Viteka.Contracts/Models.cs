namespace Viteka.Contracts;

public sealed record UtilityDefinition(string Id, string Name, string Description, string Category, bool RequiresDatabase);
public sealed record ServiceStatus(string Mode, string DatabaseStatus, bool DatabaseConfigured, string Version);
public sealed record UtilityExecutionResult(string Code, string Message);

public static class DemoCatalog
{
    // Proposals only: no utility is operational and no pharmacy data is returned.
    public static IReadOnlyList<UtilityDefinition> Items { get; } = Array.AsReadOnly(new[]
    {
        new UtilityDefinition("stock", "Analizar stock", "Consulta existencias y detecta oportunidades de mejora.", "Almacén", true),
        new UtilityDefinition("expiry", "Revisar caducidades", "Localiza productos próximos a caducar.", "Almacén", true),
        new UtilityDefinition("purchases", "Consultar compras", "Revisa compras y albaranes de proveedores.", "Compras", true)
    });
}

public sealed record PairingRequest(string Code, string DeviceName);
public sealed record PairingResponse(string DeviceId, string Token, DateTimeOffset ExpiresAt);
