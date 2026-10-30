namespace Viteka.Contracts;

public static class UtilityCatalog
{
    public const string TenenciaId = "tenencia-obligatoria";
    public static IReadOnlyList<UtilityDefinition> Items { get; } = [new(TenenciaId, "Tenencia obligatoria", "Comprueba las existencias mínimas del listado de tu provincia.", "Existencias", true)];
}
public sealed record PharmacyProfile(string Name, string Province, string Town, string Email, string Phone, string Address, string Postcode);
public sealed record TenenciaGroup(string Id, string Name, int Minimum, string[] Codes) { public Dictionary<string, string>? Descriptions { get; init; } public string? AtcPrefix { get; init; } public string? ClassificationCode { get; init; } public string? SelectionNote { get; init; } public bool RequiresReview { get; init; } public int? SourceRow { get; init; } }
public sealed record TenenciaList(int Schema, string Province, string Version, string Source, DateOnly ReviewedOn, TenenciaGroup[] Groups);
public sealed record StockArticle(string Code, string Description, decimal? Stock, string? Issue = null)
{
    /// <summary>Caducidad de la ficha de NixFarma (la más próxima si hay varias fichas).</summary>
    public DateOnly? Expiry { get; init; }
    /// <summary>Fecha de la última compra o recepción registrada en la ficha.</summary>
    public DateOnly? LastPurchase { get; init; }
    /// <summary>Fecha de la última venta registrada en la ficha.</summary>
    public DateOnly? LastSale { get; init; }
}
/// <summary>Datos opcionales de la ficha que esta instalación de NixFarma permite leer.</summary>
public sealed record TenenciaFields(bool Expiry, bool Movements, string[] Homogeneous);
public sealed record ArticleSearchResult(string Code, string Message, StockArticle[] Articles, bool Truncated = false) { public string? Group { get; init; } }
public sealed record TenenciaRow(string Id, string Name, int Minimum, decimal? Stock, decimal? Missing, string Status, StockArticle[] Articles) { public bool RequiresReview { get; init; } public string? Classification { get; init; } public string? SelectionNote { get; init; } public int? SourceRow { get; init; } }
public sealed record TenenciaResponse(string Code, string Message, string Province, string? ListVersion = null, string? Source = null, DateTimeOffset? CheckedAt = null, TenenciaRow[]? Rows = null) { public string? ProvinceName { get; init; } public TenenciaFields? Fields { get; init; } public bool Cached { get; init; } public bool Customized { get; init; } }
