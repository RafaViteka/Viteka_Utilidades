using System.Text.Json;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class TenenciaRules
{
    public static TenenciaList? Load(string directory, string province)
    {
        if (!SpanishProvinces.Items.ContainsKey(province)) throw new ArgumentException("Provincia no válida.");
        var file = Path.Combine(directory, province + ".json"); if (!File.Exists(file)) return null;
        if (new FileInfo(file).Length > 524288) throw new InvalidDataException("La lista de tenencia excede el tamaño permitido.");
        var list = JsonSerializer.Deserialize<TenenciaList>(File.ReadAllBytes(file), new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? throw new InvalidDataException();
        Validate(list); if (list.Province != province) throw new InvalidDataException("La lista no corresponde a la provincia."); return list;
    }
    public static void Validate(TenenciaList list)
    {
        if (list is null || list.Schema != 1 || !SpanishProvinces.Items.ContainsKey(list.Province ?? "") || string.IsNullOrWhiteSpace(list.Version) || list.Version.Length > 100 || string.IsNullOrWhiteSpace(list.Source) || list.Source.Length > 500 || list.ReviewedOn == default || list.ReviewedOn > DateOnly.FromDateTime(DateTime.UtcNow) || list.Groups is null || list.Groups.Length is < 1 or > 250) throw new InvalidDataException("La lista de tenencia no es válida.");
        foreach (var g in list.Groups)
        {
            if (g is null || string.IsNullOrWhiteSpace(g.Id) || g.Id.Length > 80 || string.IsNullOrWhiteSpace(g.Name) || g.Name.Length > 200 || g.Minimum is < 1 or > 10000 || g.Codes is null || g.Codes.Length > 1000 || g.Codes.Any(c => !ValidCode(c)) || g.Codes.Distinct().Count() != g.Codes.Length) throw new InvalidDataException("Revisa los grupos, códigos y unidades de la lista.");
            if (g.AtcPrefix is not null && !ValidAtc(g.AtcPrefix) || g.ClassificationCode is not null && !Regex.IsMatch(g.ClassificationCode, "^[A-Z0-9]{2,12}$") || g.SelectionNote?.Length > 600 || g.SourceRow is < 1 or > 10000) throw new InvalidDataException("Selector o nota de grupo no válido.");
            if (g.Descriptions is not null && g.Descriptions.Any(p => !g.Codes.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value) || p.Value.Length > 200)) throw new InvalidDataException("Nombre de medicamento sin código asociado.");
        }
        if (list.Groups.Select(g => g.Id).Distinct().Count() != list.Groups.Length || list.Groups.SelectMany(g => g.Codes).Distinct().Count() > 10000) throw new InvalidDataException("Demasiados códigos o grupos duplicados.");
    }
    public static bool ValidAtc(string? value) => value is not null && Regex.IsMatch(value, "^[A-Z](?:[0-9]{2}(?:[A-Z](?:[A-Z](?:[0-9]{2})?)?)?)?$", RegexOptions.CultureInvariant);
    public static bool ValidCode(string? code) => code is not null && Regex.IsMatch(code, "^[0-9]{6,7}$", RegexOptions.CultureInvariant);
    public static TenenciaRow[] Preview(TenenciaList list)
    {
        Validate(list);
        return list.Groups.Select(g => Decorate(g, new TenenciaRow(g.Id, g.Name, g.Minimum, null, null, "not_checked", g.Codes.Select(c => new StockArticle(c, g.Descriptions?.GetValueOrDefault(c) ?? ("Artículo CN " + c), null, "not_checked")).ToArray()))).ToArray();
    }
    public static TenenciaRow[] Evaluate(TenenciaList list, IReadOnlyDictionary<string, StockArticle> stocks) => Evaluate(list, stocks, null, null);
    public static TenenciaRow[] Evaluate(TenenciaList list, IReadOnlyDictionary<string, StockArticle> stocks, IReadOnlyDictionary<string, string[]>? resolved, IReadOnlyDictionary<string, string>? notes)
    {
        Validate(list);
        return list.Groups.Select(g =>
        {
            var codes = resolved?.GetValueOrDefault(g.Id) ?? g.Codes;
            var articles = codes.Select(c => stocks.TryGetValue(c, out var a) ? a with { Description = a.Description == "Artículo no identificado" ? g.Descriptions?.GetValueOrDefault(c) ?? a.Description : a.Description } : new StockArticle(c, g.Descriptions?.GetValueOrDefault(c) ?? "Artículo no identificado", null, "not_found")).ToArray();
            // Todo artículo consultado tiene existencias: si no está en NixFarma, cuenta como 0.
            // Los CN que no están dados de alta en NixFarma no se muestran (no aportan existencias).
            articles = articles.Where(a => a.Issue != "not_found").Select(a => a.Stock is null ? a with { Stock = 0 } : a).ToArray();
            decimal total = articles.Sum(a => a.Stock!.Value);
            // Solo dos estados: correcto o falta. La nota de selección del grupo se conserva como información.
            var status = total < g.Minimum ? "shortfall" : "covered";
            var row = Decorate(g, new TenenciaRow(g.Id, g.Name, g.Minimum, total, Math.Max(0, g.Minimum - total), status, articles));
            if (notes?.TryGetValue(g.Id, out var note) == true) row = row with { SelectionNote = string.Join(" ", new[] { row.SelectionNote, note }.Where(x => !string.IsNullOrWhiteSpace(x))) };
            return row;
        }).ToArray();
    }
    private static TenenciaRow Decorate(TenenciaGroup group, TenenciaRow row) => row with { Classification = group.AtcPrefix ?? group.ClassificationCode, SelectionNote = group.SelectionNote, SourceRow = group.SourceRow, RequiresReview = group.RequiresReview };
}
