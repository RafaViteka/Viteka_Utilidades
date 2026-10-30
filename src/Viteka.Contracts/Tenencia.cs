namespace Viteka.Contracts;

public static class UtilityCatalog
{
    public const string TenenciaId = "tenencia-obligatoria";
    public static IReadOnlyList<UtilityDefinition> Items { get; } = [new(TenenciaId, "Tenencia obligatoria", "Comprueba las existencias mínimas del listado de tu provincia.", "Existencias", true)];
}
public sealed record PharmacyProfile(string Name, string Province, string Town, string Email, string Phone, string Address, string Postcode);
public sealed record TenenciaGroup(string Id, string Name, int Minimum, string[] Codes) { public Dictionary<string, string>? Descriptions { get; init; } public string? AtcPrefix { get; init; } public string? ClassificationCode { get; init; } public string? SelectionNote { get; init; } public bool RequiresReview { get; init; } public int? SourceRow { get; init; } }
public sealed record TenenciaList(int Schema, string Province, string Version, string Source, DateOnly ReviewedOn, TenenciaGroup[] Groups);
public sealed record StockArticle(string Code, string Description, decimal? Stock, string? Issue = null);
public sealed record TenenciaRow(string Id, string Name, int Minimum, decimal? Stock, decimal? Missing, string Status, StockArticle[] Articles) { public bool RequiresReview { get; init; } public string? Classification { get; init; } public string? SelectionNote { get; init; } public int? SourceRow { get; init; } }
public sealed record TenenciaResponse(string Code, string Message, string Province, string? ListVersion = null, string? Source = null, DateTimeOffset? CheckedAt = null, TenenciaRow[]? Rows = null) { public string? ProvinceName { get; init; } }
