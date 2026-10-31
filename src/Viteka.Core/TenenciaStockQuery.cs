using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class TenenciaStockQuery
{
    public sealed record Schema(string ArticleCode, string Description, string StockCode, string Stock)
    {
        public string ArticleTable { get; init; } = "ARTICULOS"; public string StockTable { get; init; } = "EXISTENCIAS"; public string? Atc { get; init; }
        public string[] ArticleColumns { get; init; } = []; public string[] AtcCandidates { get; init; } = [];
        public string[] StockColumns { get; init; } = [];
        /// <summary>Columnas opcionales de la ficha de existencias (identificadores entre comillas) o null si no se identifican sin ambigüedad.</summary>
        public string? Expiry { get; init; }
        public string? LastPurchase { get; init; }
        public string? LastSale { get; init; }
        /// <summary>Columnas de AB_ARTICULOS que contienen la agrupación o conjunto homogéneo.</summary>
        public string[] Homogeneous { get; init; } = [];
        public Viteka.Contracts.TenenciaFields Fields => new(Expiry is not null, LastPurchase is not null || LastSale is not null, Homogeneous.Select(h => h.Trim('"')).ToArray()) { Purchase=LastPurchase is not null, PurchaseColumn=LastPurchase?.Trim('"') };
    }
    private static string Normalize(string column) => column.Replace("_", "").Replace(" ", "").ToUpperInvariant();
    private static bool SafeName(string column) => Regex.IsMatch(column, "^[A-Za-z][A-Za-z0-9_ ]{0,127}$");
    /// <summary>
    /// Elige una columna opcional por su nombre: si hay una sola candidata se usa; si hay varias, solo se acepta una
    /// preferida inequívoca. Ante la duda no se lee nada (se mostrará «sin fecha»).
    /// </summary>
    internal static string? Pick(string[] columns, Func<string, bool> candidate, params string[] preferred)
    {
        var hits = columns.Where(c => SafeName(c) && candidate(Normalize(c))).ToArray();
        if (hits.Length == 1) return Quote(hits[0]);
        var best = hits.Where(c => preferred.Contains(Normalize(c))).ToArray();
        return best.Length == 1 ? Quote(best[0]) : null;
    }
    internal static bool IsExpiry(string n) => n.Contains("CADUC", StringComparison.Ordinal) && !Regex.IsMatch(n, "MES|DIAS|AVIS|ALERT|CONTROL|CTRL|LOTE|BORR");
    private static bool IsDate(string n) => Regex.IsMatch(n, "FEC|FECHA|DATE|^F") && !Regex.IsMatch(n, "PRECIO|PVP|IMP|CANT|UNID|COSTE|PROV|NUM|USU");
    internal static bool IsLastPurchase(string n) => n.Contains("ULT", StringComparison.Ordinal) && Regex.IsMatch(n, "COMPRA") && IsDate(n);
    internal static bool IsLastSale(string n) => n.Contains("ULT", StringComparison.Ordinal) && Regex.IsMatch(n, "VENTA|SALIDA|DISPENS|VTA") && IsDate(n);
    /// <summary>Convierte fechas de NixFarma (fecha, texto o número) a DateOnly. Si solo hay mes y año, cuenta el último día del mes.</summary>
    public static DateOnly? ParseDate(object? value)
    {
        static DateOnly? Valid(DateOnly d) => d.Year is >= 1990 and <= 2100 ? d : null;
        static DateOnly? Month(int year, int month) => month is >= 1 and <= 12 && year is >= 1990 and <= 2100 ? new DateOnly(year, month, DateTime.DaysInMonth(year, month)) : null;
        switch (value)
        {
            case null or DBNull: return null;
            case DateTime dt: return Valid(DateOnly.FromDateTime(dt));
            case DateTimeOffset dto: return Valid(DateOnly.FromDateTime(dto.DateTime));
            case DateOnly d: return Valid(d);
        }
        var text = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "";
        if (text.Length == 0) return null;
        if (Regex.IsMatch(text, @"^\d+(\.0+)?$"))
        {
            var digits = text.Split('.')[0];
            int Part(int start, int length) => int.Parse(digits.AsSpan(start, length), CultureInfo.InvariantCulture);
            if (digits.Length == 8 && DateOnly.TryParseExact(digits, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var full)) return Valid(full);
            if (digits.Length == 6 && digits[..2] is "19" or "20" && Part(4, 2) is >= 1 and <= 12) return Month(Part(0, 4), Part(4, 2));
            if (digits.Length == 6) { var day = Part(4, 2); if (day == 0) return Month(2000 + Part(0, 2), Part(2, 2)); return DateOnly.TryParseExact(digits, "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var shortDate) ? Valid(shortDate) : null; }
            if (digits.Length == 4) return Month(2000 + Part(0, 2), Part(2, 2));
            return null;
        }
        string[] days = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd/MM/yy", "dd.MM.yyyy", "yyyy/MM/dd"];
        var head = text.Split(' ', 'T')[0];
        if (DateOnly.TryParseExact(head, days, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) return Valid(parsed);
        var month = Regex.Match(text, @"^(\d{1,2})[/-](\d{2}|\d{4})$");
        if (month.Success) { var y = int.Parse(month.Groups[2].Value, CultureInfo.InvariantCulture); return Month(y < 100 ? 2000 + y : y, int.Parse(month.Groups[1].Value, CultureInfo.InvariantCulture)); }
        return DateTime.TryParse(text, CultureInfo.GetCultureInfo("es-ES"), DateTimeStyles.None, out var loose) ? Valid(DateOnly.FromDateTime(loose)) : null;
    }
    public static Schema Inspect(DbConnection connection, DbTransaction transaction, bool nativeNixFarma = false)
    {
        string[] Columns(string table) { using var cmd = Command(connection, transaction, "SELECT * FROM " + table + " WHERE 1 = 0"); using var reader = cmd.ExecuteReader(); return Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToArray(); }
        string Find(string[] columns, params string[] names) { var hits = columns.Where(c => names.Contains(c.Replace("_", "").Replace(" ", "").ToUpperInvariant())).ToArray(); if (hits.Length != 1 || !Regex.IsMatch(hits[0], "^[A-Za-z][A-Za-z0-9_ ]{0,127}$")) throw new InvalidDataException("No se identifica de forma única la columna de artículos o stock. Viteka debe revisar la estructura."); return "\"" + hits[0] + "\""; }
        var articleTable = nativeNixFarma ? "APPUL.AB_ARTICULOS" : "ARTICULOS"; var stockTable = nativeNixFarma ? "APPUL.AB_ARTICULOS_FICHA_E" : "EXISTENCIAS";
        var a = Columns(articleTable); var e = Columns(stockTable);
        var candidates = a.Where(c => Regex.IsMatch(c, "^[A-Za-z][A-Za-z0-9_ ]{0,127}$") && Regex.IsMatch(c, "ATC|TERAP|GRUPO", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var exact = candidates.Where(c => new[] { "ATC", "CODIGOATC", "GRUPOATC" }.Contains(c.Replace("_", "").Replace(" ", "").ToUpperInvariant())).ToArray();
        var homogeneous = a.Where(c => SafeName(c) && Normalize(c).Contains("HOMOG", StringComparison.Ordinal)).Take(6).Select(Quote).ToArray();
        return new(Find(a, nativeNixFarma ? "CODIGO" : "ARTICULO"), Find(a, "DESCRIPCION"), Find(e, nativeNixFarma ? "ARTCODIGO" : "ARTICULO"), Find(e, nativeNixFarma ? ["EXIACTUALES"] : ["ACTUAL", "STOCKACTUAL"]))
        {
            ArticleTable = articleTable,
            StockTable = stockTable,
            Atc = exact.Length == 1 ? Quote(exact[0]) : null,
            ArticleColumns = a,
            AtcCandidates = candidates.Select(Quote).ToArray(),
            StockColumns = e,
            // La caducidad y las últimas compras/ventas se leen de la ficha de existencias.
            Expiry = Pick(e, IsExpiry, "FECCADUCIDAD", "FECHACADUCIDAD", "CADUCIDAD", "FCADUCIDAD", "FECCAD"),
            LastPurchase = Pick(e, IsLastPurchase, "FECULTCOMPRA", "FECHAULTIMACOMPRA", "FECULTIMACOMPRA", "FECHAULTCOMPRA", "FULTCOMPRA"),
            LastSale = Pick(e, IsLastSale, "FECULTVENTA", "FECHAULTIMAVENTA", "FECULTIMAVENTA", "FECHAULTVENTA", "FULTVENTA"),
            Homogeneous = homogeneous,
        };
    }
    public static T ReadOnly<T>(DbConnection connection, Func<DbTransaction, T> action)
    {
        connection.Open(); using var tx = connection.BeginTransaction(); try { using var readOnly = Command(connection, tx, "SET TRANSACTION READ ONLY"); readOnly.ExecuteNonQuery(); return action(tx); } finally { try { tx.Rollback(); } catch (DbException) { } }
    }
    public static Dictionary<string, StockArticle> Read(DbConnection connection, DbTransaction tx, Schema schema, string[] codes, CancellationToken ct = default)
    {
        if (codes.Length is < 1 or > 10000 || codes.Any(c => !TenenciaRules.ValidCode(c))) throw new ArgumentException("Códigos no válidos.");
        var descriptions = new Dictionary<string, string>(); var values = new Dictionary<string, decimal?>(); var issues = new Dictionary<string, string>(); var count = 0;
        var expiry = new Dictionary<string, DateOnly>(); var purchase = new Dictionary<string, DateOnly>(); var sale = new Dictionary<string, DateOnly>();
        var extras = new[] { schema.Expiry, schema.LastPurchase, schema.LastSale }.Where(x => x is not null).Select(x => x!).ToArray();
        foreach (var batch in codes.Distinct().Chunk(250))
        {
            ct.ThrowIfCancellationRequested();
            using (var cmd = Select(connection, tx, schema.ArticleTable, schema.ArticleCode, schema.Description, batch))
            using (var reader = cmd.ExecuteReader()) while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 100000) throw new InvalidDataException("Demasiadas filas: revisa el alcance de la consulta."); var code = Code(reader.GetValue(0)); if (!batch.Contains(code)) throw new InvalidDataException();
                if (!descriptions.TryAdd(code, reader.IsDBNull(1) ? "Sin descripción" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)![..Math.Min(200, Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!.Length)])) issues[code] = "duplicate_article";
            }
            using (var cmd = Select(connection, tx, schema.StockTable, schema.StockCode, schema.Stock, batch, extras))
            using (var reader = cmd.ExecuteReader()) while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 100000) throw new InvalidDataException("Demasiadas filas: revisa el alcance de la consulta."); var code = Code(reader.GetValue(0)); if (!batch.Contains(code)) throw new InvalidDataException();
                decimal? stock = reader.IsDBNull(1) ? null : Convert.ToDecimal(reader.GetValue(1), CultureInfo.InvariantCulture);
                // Varias filas del mismo CN (p. ej. por almacén): se suman; un stock vacío cuenta como 0.
                if (!values.TryAdd(code, stock)) { values[code] = (values[code] ?? 0) + (stock ?? 0); issues[code] = "summed_stock"; }
                // Varias fichas: la caducidad más próxima y el movimiento más reciente.
                var column = 2;
                if (schema.Expiry is not null && ParseDate(reader.GetValue(column++)) is { } exp && (!expiry.TryGetValue(code, out var previous) || exp < previous)) expiry[code] = exp;
                if (schema.LastPurchase is not null && ParseDate(reader.GetValue(column++)) is { } buy && (!purchase.TryGetValue(code, out var lastBuy) || buy > lastBuy)) purchase[code] = buy;
                if (schema.LastSale is not null && ParseDate(reader.GetValue(column++)) is { } sold && (!sale.TryGetValue(code, out var lastSold) || sold > lastSold)) sale[code] = sold;
            }
        }
        return codes.Distinct().ToDictionary(c => c, c =>
        {
            // Nunca quedan existencias pendientes: un CN que no está dado de alta, sin fila de existencias o con stock
            // vacío tiene 0 unidades en la farmacia. El motivo se conserva como información.
            var issue = !descriptions.ContainsKey(c) ? "not_found" : !values.ContainsKey(c) ? "stock_not_found" : values[c] is null ? "stock_null" : issues.GetValueOrDefault(c);
            return new StockArticle(c, descriptions.GetValueOrDefault(c, "Artículo no identificado"), issue == "not_found" ? 0 : values.GetValueOrDefault(c) ?? 0, issue)
            { Expiry = expiry.TryGetValue(c, out var e) ? e : null, LastPurchase = purchase.TryGetValue(c, out var b) ? b : null, LastSale = sale.TryGetValue(c, out var v) ? v : null };
        });
    }
    internal static string Quote(string identifier) { if (!Regex.IsMatch(identifier, "^[A-Za-z][A-Za-z0-9_ ]{0,127}$")) throw new InvalidDataException(); return "\"" + identifier + "\""; }
    private static string Code(object value) { var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""; if (!TenenciaRules.ValidCode(s)) throw new InvalidDataException("Código nacional no reconocido."); return s; }
    private static DbCommand Select(DbConnection connection, DbTransaction tx, string table, string code, string value, string[] codes, string[]? extras = null)
    {
        extras ??= [];
        // Identifiers originate exclusively in Inspect; values always use positional ODBC parameters.
        foreach (var identifier in new[] { code, value }.Concat(extras)) if (!Regex.IsMatch(identifier, "^\"[A-Za-z][A-Za-z0-9_ ]{0,127}\"$")) throw new ArgumentException();
        if (table is not ("ARTICULOS" or "EXISTENCIAS" or "APPUL.AB_ARTICULOS" or "APPUL.AB_ARTICULOS_FICHA_E")) throw new ArgumentException();
        var cmd = Command(connection, tx, $"SELECT {string.Join(", ", new[] { code, value }.Concat(extras))} FROM {table} WHERE {code} IN ({string.Join(",", codes.Select(_ => "?"))})"); foreach (var c in codes) { var p = cmd.CreateParameter(); p.DbType = DbType.String; p.Value = c; cmd.Parameters.Add(p); }
        return cmd;
    }
    internal static DbCommand Command(DbConnection connection, DbTransaction tx, string text) { var cmd = connection.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 15; cmd.CommandText = text; return cmd; }
}
