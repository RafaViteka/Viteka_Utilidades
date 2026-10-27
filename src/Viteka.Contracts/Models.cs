namespace Viteka.Contracts;

public sealed record UtilityDefinition(string Id, string Name, string Description, string Category, bool RequiresDatabase);
public sealed record ServiceStatus(string Mode, string DatabaseStatus, bool DatabaseConfigured, string Version);
public sealed record UtilityExecutionResult(string Code, string Message);

public static class DemoCatalog
{
    // MVP shell only. Utilities will be added after visual approval.
    public static IReadOnlyList<UtilityDefinition> Items { get; } = Array.Empty<UtilityDefinition>();
}

public sealed record PairingRequest(string Code, string DeviceName);
public sealed record PairingResponse(string DeviceId, string Token, DateTimeOffset ExpiresAt);
