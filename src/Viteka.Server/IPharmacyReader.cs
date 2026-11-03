using Viteka.Contracts;
namespace Viteka.Server;

// No arbitrary SQL boundary. Add typed read operations only after agreeing the first utility.
public interface IPharmacyReader
{
    Task<UtilityExecutionResult> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

public sealed class UnconfiguredPharmacyReader : IPharmacyReader
{
    public Task<UtilityExecutionResult> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new UtilityExecutionResult("database_not_configured",
            "Nixfarma no está configurado. No se ha ejecutado ninguna consulta."));
    }
}
