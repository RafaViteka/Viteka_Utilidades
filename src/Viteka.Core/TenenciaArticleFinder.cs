using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

/// <summary>
/// Búsqueda de solo lectura de artículos de NixFarma para completar los grupos de tenencia:
/// por código nacional, por descripción o por agrupación homogénea. Siempre con parámetros y límite de filas.
/// </summary>
public static class TenenciaArticleFinder
{
    public const int Limit = 200;
    public static ArticleSearchResult Search(DbConnection connection, DbTransaction tx, TenenciaStockQuery.Schema schema, string mode, string text, string? field, CancellationToken ct = default)
    {
        text = (text ?? "").Trim();
        if (schema.ArticleTable is not ("ARTICULOS" or "APPUL.AB_ARTICULOS")) throw new InvalidDataException("Tabla de artículos no válida.");
        if(mode.StartsWith("therapeutic.",StringComparison.Ordinal))return TherapeuticFinder.Search(connection,tx,schema,mode,text,field,ct);
        switch (mode)
        {
            case "code":
                {
                    var codes = Regex.Matches(text, "[0-9]{6,7}").Select(m => m.Value).Distinct().ToArray();
                    if (codes.Length == 0) return new("empty", "Escribe o pega uno o varios códigos nacionales.", []);
                    if (codes.Length > 1000) throw new ArgumentException("Máximo 1.000 códigos por búsqueda.");
                    var found = TenenciaStockQuery.Read(connection, tx, schema, codes, ct);
                    var articles = codes.Select(c => found[c]).ToArray();
                    var missing = articles.Count(a => a.Issue == "not_found");
                    return new("ok", articles.Length + " códigos" + (missing > 0 ? " · " + missing + " no están dados de alta en Nixfarma" : ""), articles);
                }
            case "description":
                {
                    var words = Regex.Split(text.ToUpperInvariant(), "\\s+").Where(w => w.Length >= 2).Take(5).ToArray();
                    if (words.Length == 0 || words.Any(w => w.Length > 40)) return new("empty", "Escribe al menos 2 letras de la descripción.", []);
                    var predicate = string.Join(" AND ", words.Select(_ => $"UPPER({schema.Description}) LIKE ?"));
                    return Find(connection, tx, schema, predicate, words.Select(w => "%" + w.Replace("%", "").Replace("_", "") + "%").ToArray(), "descripción «" + text + "»", null, ct);
                }
            case "homogeneous":
                {
                    if (field is null || !schema.Homogeneous.Contains("\"" + field + "\"")) return new("unavailable", "No se ha identificado en Nixfarma la columna de agrupación homogénea. Viteka debe revisarla en Comprobar tablas.", []);
                    if (text.Length is < 1 or > 40 || text.Any(char.IsControl)) return new("empty", "Escribe el código de la agrupación homogénea.", []);
                    var column = "\"" + field + "\"";
                    return Find(connection, tx, schema, $"UPPER(TRIM({column})) = ?", [text.ToUpperInvariant()], "agrupación homogénea " + text, column, ct);
                }
            default: throw new ArgumentException("Tipo de búsqueda no válido.");
        }
    }
    private static ArticleSearchResult Find(DbConnection connection, DbTransaction tx, TenenciaStockQuery.Schema schema, string predicate, string[] values, string label, string? groupColumn, CancellationToken ct)
    {
        // Identificadores procedentes de Inspect, validados; los valores van siempre como parámetros.
        foreach (var identifier in new[] { schema.ArticleCode, schema.Description }.Concat(groupColumn is null ? [] : [groupColumn]))
            if (!Regex.IsMatch(identifier, "^\"[A-Za-z][A-Za-z0-9_ ]{0,127}\"$")) throw new InvalidDataException("Nombre de columna no válido.");
        using var cmd = TenenciaStockQuery.Command(connection, tx, $"SELECT {schema.ArticleCode}, {schema.Description} FROM {schema.ArticleTable} WHERE {predicate} AND ROWNUM <= {Limit + 1}");
        foreach (var value in values) { var p = cmd.CreateParameter(); p.DbType = DbType.String; p.Value = value; cmd.Parameters.Add(p); }
        var codes = new List<string>(); var truncated = false;
        using (var reader = cmd.ExecuteReader())
            while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (codes.Count == Limit) { truncated = true; break; }
                var code = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? "";
                if (TenenciaRules.ValidCode(code) && !codes.Contains(code)) codes.Add(code);
            }
        if (codes.Count == 0) return new("ok", "Sin artículos para " + label + ".", []);
        var stocks = TenenciaStockQuery.Read(connection, tx, schema, codes.ToArray(), ct);
        var articles = codes.Select(c => stocks[c]).OrderByDescending(a => a.Stock ?? 0).ThenBy(a => a.Description, StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), true)).ToArray();
        return new("ok", articles.Length + (truncated ? "+ artículos (se muestran los " + Limit + " primeros; afina la búsqueda)" : " artículos") + " para " + label + ".", articles, truncated);
    }
}
