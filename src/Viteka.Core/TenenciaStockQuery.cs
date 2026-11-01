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
        public string? MinimumStock { get; init; }
        public string? SupplierCode { get; init; }
        public string? AuxiliaryStockTable { get; init; }
        public string? AuxiliaryStockCode { get; init; }
        public string? AuxiliaryExpiry { get; init; }
        public string? AuxiliaryLastPurchase { get; init; }
        public string? AuxiliaryLastSale { get; init; }
        public string? AuxiliaryMinimumStock { get; init; }
        public string? AuxiliarySupplierCode { get; init; }
        public string? ProviderTable { get; init; }
        public string? ProviderCode { get; init; }
        public string? ProviderDescription { get; init; }
        public string? DetailsTable { get; init; }
        public string? DetailsCode { get; init; }
        public string? DetailsClass { get; init; }
        public string? LaboratoryCode { get; init; }
        public string? AbcCode { get; init; }
        public string? Pvp { get; init; }
        public string? LaboratoryTable { get; init; }
        public string? LaboratoryClass { get; init; }
        public string? LaboratoryKey { get; init; }
        public string? LaboratoryDescription { get; init; }
        public string? Inactive { get; init; }
        public string? InactiveOn { get; init; }
        public string? HomogeneousDetailTable { get; init; }
        public string? HomogeneousArticleCode { get; init; }
        public string? HomogeneousCommunity { get; init; }
        public string? HomogeneousGroupCode { get; init; }
        public string? HomogeneousMasterTable { get; init; }
        public string? HomogeneousMasterCode { get; init; }
        public string? HomogeneousMasterCommunity { get; init; }
        public string? HomogeneousDescription { get; init; }
        public string? BarcodeTable { get; init; }
        public string? BarcodeArticleCode { get; init; }
        public string? BarcodeValue { get; init; }
        public string? RelativeBarcodeTable { get; init; }
        public string? RelativeBarcodeArticleCode { get; init; }
        public string? RelativeBarcodeValue { get; init; }
        /// <summary>Columnas de AB_ARTICULOS que contienen la agrupación o conjunto homogéneo.</summary>
        public string[] Homogeneous { get; init; } = [];
        public Viteka.Contracts.TenenciaFields Fields => new(Expiry is not null || AuxiliaryExpiry is not null, LastPurchase is not null || LastSale is not null || AuxiliaryLastPurchase is not null || AuxiliaryLastSale is not null, Homogeneous.Select(h => h.Trim('"')).ToArray()) { Purchase=LastPurchase is not null || AuxiliaryLastPurchase is not null, PurchaseColumn=(LastPurchase??AuxiliaryLastPurchase)?.Trim('"') };
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
    internal static string? Exact(string[] columns, params string[] names)
    {
        var hits = columns.Where(c => SafeName(c) && names.Contains(Normalize(c))).ToArray();
        return hits.Length == 1 ? Quote(hits[0]) : null;
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
        (string Table, string[] Columns) FirstAvailable(params string[] tables)
        {
            DbException? last = null;
            foreach (var table in tables) try { return (table, Columns(table)); } catch (DbException error) { last = error; }
            if (last is not null) throw last;
            throw new InvalidDataException("No se encuentra la tabla de artículos o existencias de NixFarma.");
        }
        (string Table, string[] Columns)? Optional(params string[] tables)
        {
            foreach (var table in tables) try { return (table, Columns(table)); } catch (DbException) { }
            return null;
        }
        (string Table, string[] Columns)? OptionalOther(string excluded, params string[] tables)
        {
            foreach (var table in tables.Where(t=>t!=excluded)) try { var columns=Columns(table);if(columns.Any(c=>Normalize(c) is "STOCKMIN" or "PROVULTCOMPRA"))return (table,columns); } catch (DbException) { }
            return null;
        }
        string Find(string[] columns, params string[] names) { var hits = columns.Where(c => names.Contains(c.Replace("_", "").Replace(" ", "").ToUpperInvariant())).ToArray(); if (hits.Length != 1 || !Regex.IsMatch(hits[0], "^[A-Za-z][A-Za-z0-9_ ]{0,127}$")) throw new InvalidDataException("No se identifica de forma única la columna de artículos o stock. Viteka debe revisar la estructura."); return "\"" + hits[0] + "\""; }
        var article = nativeNixFarma ? FirstAvailable("APADMIN_AB_ARTICULOS", "APADMIN.AB_ARTICULOS", "APPUL.AB_ARTICULOS") : FirstAvailable("ARTICULOS");
        // AC1 aporta el stock que ya está confirmado en las instalaciones probadas. Las vistas APADMIN/AC
        // se usan como fuente auxiliar para completar mínimo, proveedor y movimientos cuando sea necesario.
        var stock = nativeNixFarma ? FirstAvailable("AC1_EXISTENCIAS", "APADMIN_AC_EXISTENCIAS", "APADMIN.AC_EXISTENCIAS", "AC_EXISTENCIAS", "APPUL.AB_ARTICULOS_FICHA_E") : FirstAvailable("EXISTENCIAS");
        var articleTable = article.Table; var stockTable = stock.Table; var a = article.Columns; var e = stock.Columns;
        var auxiliaryStock = nativeNixFarma && (Exact(e,"STOCKMIN") is null || Exact(e,"PROVULTCOMPRA") is null) ? OptionalOther(stockTable,"APADMIN_AC_EXISTENCIAS", "APADMIN.AC_EXISTENCIAS", "AC_EXISTENCIAS", "AC1_EXISTENCIAS") : null;
        var details = nativeNixFarma ? Optional("APADMIN_AB_ARTICULOS_FICHA_E", "APADMIN.AB_ARTICULOS_FICHA_E") : null;
        var providers = nativeNixFarma ? Optional("APADMIN_AD_PROVEEDORES", "APADMIN.AD_PROVEEDORES") : null;
        var laboratories = nativeNixFarma ? Optional("DW_AB_LABORATORIOS", "DW.AB_LABORATORIOS") : null;
        var homogeneousDetail = nativeNixFarma ? Optional("APPUL_AB_CJTO_DET_ESPECIALIDAD", "APPUL.AB_CJTO_DET_ESPECIALIDAD") : null;
        var homogeneousMaster = nativeNixFarma ? Optional("APPUL_AB_CJTO_ESPECIALIDAD", "APPUL.AB_CJTO_ESPECIALIDAD") : null;
        var barcodes = nativeNixFarma ? Optional("DSP_AB_ARTICULOS", "DSP.AB_ARTICULOS") : null;
        var relativeBarcodes = nativeNixFarma ? Optional("DONET_AD_CRELATIVOS", "DONET.AD_CRELATIVOS") : null;
        var candidates = a.Where(c => Regex.IsMatch(c, "^[A-Za-z][A-Za-z0-9_ ]{0,127}$") && Regex.IsMatch(c, "ATC|TERAP|GRUPO", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
        var exact = candidates.Where(c => new[] { "ATC", "CODIGOATC", "GRUPOATC" }.Contains(c.Replace("_", "").Replace(" ", "").ToUpperInvariant())).ToArray();
        var homogeneous = a.Where(c => SafeName(c) && Normalize(c).Contains("HOMOG", StringComparison.Ordinal)).Take(6).Select(Quote).ToArray();
        return new(Find(a, nativeNixFarma ? ["CODIGO", "ARTICULO", "ARTCODIGO"] : ["ARTICULO"]), Find(a, "DESCRIPCION"), Find(e, nativeNixFarma ? ["ARTCODIGO", "ARTICULO", "CODIGO"] : ["ARTICULO"]), Find(e, nativeNixFarma ? ["EXIACTUALES", "ACTUAL", "STOCKACTUAL"] : ["ACTUAL", "STOCKACTUAL"]))
        {
            ArticleTable = articleTable,
            StockTable = stockTable,
            Atc = exact.Length == 1 ? Quote(exact[0]) : null,
            ArticleColumns = a,
            AtcCandidates = candidates.Select(Quote).ToArray(),
            StockColumns = e,
            // La caducidad y las últimas compras/ventas se leen de la ficha de existencias.
            Expiry = Pick(e, IsExpiry, "FECCADUCIDAD", "FECHACADUCIDAD", "CADUCIDAD", "FCADUCIDAD", "FECCAD"),
            LastPurchase = Exact(e, "FUC") ?? Pick(e, IsLastPurchase, "FECULTCOMPRA", "FECHAULTIMACOMPRA", "FECULTIMACOMPRA", "FECHAULTCOMPRA", "FULTCOMPRA"),
            LastSale = Exact(e, "FUV") ?? Pick(e, IsLastSale, "FECULTVENTA", "FECHAULTIMAVENTA", "FECULTIMAVENTA", "FECHAULTVENTA", "FULTVENTA"),
            MinimumStock = Exact(e, "STOCKMIN"),
            SupplierCode = Exact(e, "PROVULTCOMPRA"),
            AuxiliaryStockTable = auxiliaryStock?.Table,
            AuxiliaryStockCode = auxiliaryStock is null ? null : Exact(auxiliaryStock.Value.Columns,"ARTCODIGO","ARTICULO","CODIGO"),
            AuxiliaryExpiry = auxiliaryStock is null ? null : Pick(auxiliaryStock.Value.Columns,IsExpiry,"FECCADUCIDAD","FECHACADUCIDAD","CADUCIDAD","FCADUCIDAD","FECCAD"),
            AuxiliaryLastPurchase = auxiliaryStock is null ? null : Exact(auxiliaryStock.Value.Columns,"FUC") ?? (auxiliaryStock is null ? null : Pick(auxiliaryStock.Value.Columns,IsLastPurchase)),
            AuxiliaryLastSale = auxiliaryStock is null ? null : Exact(auxiliaryStock.Value.Columns,"FUV") ?? (auxiliaryStock is null ? null : Pick(auxiliaryStock.Value.Columns,IsLastSale)),
            AuxiliaryMinimumStock = auxiliaryStock is null ? null : Exact(auxiliaryStock.Value.Columns,"STOCKMIN"),
            AuxiliarySupplierCode = auxiliaryStock is null ? null : Exact(auxiliaryStock.Value.Columns,"PROVULTCOMPRA"),
            ProviderTable = providers?.Table,
            ProviderCode = providers is null ? null : Exact(providers.Value.Columns, "CODIGO", "PROVCODIGO", "PROVEEDOR", "PROVCOD"),
            ProviderDescription = providers is null ? null : Exact(providers.Value.Columns, "DESCRIPCION", "PROVDESCRIPCION", "NOMBRE", "RAZONSOCIAL"),
            DetailsTable = details?.Table,
            DetailsCode = details is null ? null : Exact(details.Value.Columns, "ARTCODIGO", "ARTICULO", "CODIGO"),
            DetailsClass = details is null ? null : Exact(details.Value.Columns, "ARTCLASE", "CLASE"),
            LaboratoryCode = details is null ? null : Exact(details.Value.Columns, "ARTLABCODIGO"),
            AbcCode = details is null ? null : Exact(details.Value.Columns, "CODIGOABC"),
            Pvp = details is null ? null : Exact(details.Value.Columns, "ARTPVPEUROS"),
            LaboratoryTable = laboratories?.Table,
            LaboratoryClass = laboratories is null ? null : Exact(laboratories.Value.Columns, "CLASE"),
            LaboratoryKey = laboratories is null ? null : Exact(laboratories.Value.Columns, "CODIGO", "LABCODIGO"),
            LaboratoryDescription = laboratories is null ? null : Exact(laboratories.Value.Columns, "DESCRIPCION", "LABDESCRIPCION", "NOMBRE"),
            Inactive = Exact(a, "BAJA"),
            InactiveOn = Pick(a, n => n.Contains("BAJA", StringComparison.Ordinal) && IsDate(n), "FECHABAJA", "FECBAJA", "FBAJA"),
            HomogeneousDetailTable = homogeneousDetail?.Table,
            HomogeneousArticleCode = homogeneousDetail is null ? null : Exact(homogeneousDetail.Value.Columns, "ARTCOD", "ARTCODIGO", "ARTICULO"),
            HomogeneousCommunity = homogeneousDetail is null ? null : Exact(homogeneousDetail.Value.Columns, "COMUNIDAD", "ACOMUNIDAD"),
            HomogeneousGroupCode = homogeneousDetail is null ? null : Exact(homogeneousDetail.Value.Columns, "CJTOCOD"),
            HomogeneousMasterTable = homogeneousMaster?.Table,
            HomogeneousMasterCode = homogeneousMaster is null ? null : Exact(homogeneousMaster.Value.Columns, "CJTOCOD", "CODIGO"),
            HomogeneousMasterCommunity = homogeneousMaster is null ? null : Exact(homogeneousMaster.Value.Columns, "COMUNIDAD", "ACOMUNIDAD"),
            HomogeneousDescription = homogeneousMaster is null ? null : Exact(homogeneousMaster.Value.Columns, "DESCRIPCION"),
            BarcodeTable = barcodes?.Table,
            BarcodeArticleCode = barcodes is null ? null : Exact(barcodes.Value.Columns, "ARTCODIGO", "ARTCOD", "ARTICULO", "CODIGO"),
            BarcodeValue = barcodes is null ? null : Exact(barcodes.Value.Columns, "EAN13"),
            RelativeBarcodeTable = relativeBarcodes?.Table,
            RelativeBarcodeArticleCode = relativeBarcodes is null ? null : Exact(relativeBarcodes.Value.Columns, "ARTCODIGO", "ARTCOD", "ARTICULO"),
            RelativeBarcodeValue = relativeBarcodes is null ? null : Exact(relativeBarcodes.Value.Columns, "CODRELATIVO"),
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
        var minimum = new Dictionary<string, decimal>(); var supplier = new Dictionary<string, string>(); var inactive = new HashSet<string>(); var inactiveOn = new Dictionary<string, DateOnly>();
        var details = new Dictionary<string, (string Class, string Laboratory, string Abc, decimal? Pvp)>();
        var stockExtras = new[] { schema.Expiry, schema.LastPurchase, schema.LastSale, schema.MinimumStock, schema.SupplierCode }.Where(x => x is not null).Select(x => x!).ToArray();
        var articleExtras = new[] { schema.Inactive, schema.InactiveOn }.Where(x => x is not null).Select(x => x!).ToArray();
        foreach (var batch in codes.Distinct().Chunk(250))
        {
            ct.ThrowIfCancellationRequested();
            using (var cmd = Select(connection, tx, schema.ArticleTable, schema.ArticleCode, schema.Description, batch, articleExtras))
            using (var reader = cmd.ExecuteReader()) while (reader.Read())
            {
                ct.ThrowIfCancellationRequested();
                if (++count > 100000) throw new InvalidDataException("Demasiadas filas: revisa el alcance de la consulta."); var code = Code(reader.GetValue(0)); if (!batch.Contains(code)) throw new InvalidDataException();
                if (!descriptions.TryAdd(code, reader.IsDBNull(1) ? "Sin descripción" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)![..Math.Min(200, Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)!.Length)])) issues[code] = "duplicate_article";
                var articleColumn = 2;
                if (schema.Inactive is not null && string.Equals(Convert.ToString(reader.GetValue(articleColumn++), CultureInfo.InvariantCulture)?.Trim(), "S", StringComparison.OrdinalIgnoreCase)) inactive.Add(code);
                if (schema.InactiveOn is not null && ParseDate(reader.GetValue(articleColumn++)) is { } off) inactiveOn[code] = off;
            }
            using (var cmd = Select(connection, tx, schema.StockTable, schema.StockCode, schema.Stock, batch, stockExtras))
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
                if (schema.MinimumStock is not null && !reader.IsDBNull(column) && decimal.TryParse(Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var min)) minimum[code] = min;
                if (schema.MinimumStock is not null) column++;
                if (schema.SupplierCode is not null && !reader.IsDBNull(column)) supplier[code] = Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)?.Trim() ?? "";
            }
            var detailColumns = new[] { schema.DetailsClass, schema.LaboratoryCode, schema.AbcCode, schema.Pvp }.Where(x => x is not null).Select(x => x!).ToArray();
            if (schema.DetailsTable is not null && schema.DetailsCode is not null && detailColumns.Length > 0)
            using (var cmd = Select(connection, tx, schema.DetailsTable, schema.DetailsCode, detailColumns[0], batch, detailColumns.Skip(1).ToArray()))
            using (var reader = cmd.ExecuteReader()) while (reader.Read())
            {
                var code = Code(reader.GetValue(0)); var column = 1;
                string ReadText(string? field) => field is null ? "" : reader.IsDBNull(column) ? Advance("") : Advance(Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture)?.Trim() ?? "");
                string Advance(string value) { column++; return value; }
                var @class = ReadText(schema.DetailsClass); var laboratory = ReadText(schema.LaboratoryCode); var abc = ReadText(schema.AbcCode);
                decimal? pvp = null; if (schema.Pvp is not null) { if (!reader.IsDBNull(column) && decimal.TryParse(Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)) pvp = amount; column++; }
                details[code] = (@class, laboratory, abc, pvp);
            }
        }
        var auxiliaryExtras = new[] { schema.AuxiliaryExpiry, schema.AuxiliaryLastPurchase, schema.AuxiliaryLastSale, schema.AuxiliaryMinimumStock, schema.AuxiliarySupplierCode }.Where(x=>x is not null).Select(x=>x!).ToArray();
        if(schema.AuxiliaryStockTable is not null && schema.AuxiliaryStockCode is not null && auxiliaryExtras.Length>0)
        foreach(var batch in codes.Distinct().Chunk(250))
        using(var cmd=Select(connection,tx,schema.AuxiliaryStockTable,schema.AuxiliaryStockCode,auxiliaryExtras[0],batch,auxiliaryExtras.Skip(1).ToArray()))
        using(var reader=cmd.ExecuteReader())while(reader.Read())
        {
            var code=Code(reader.GetValue(0));var column=1;
            if(schema.AuxiliaryExpiry is not null&&ParseDate(reader.GetValue(column++)) is {} exp&&(!expiry.TryGetValue(code,out var priorExp)||exp<priorExp))expiry[code]=exp;
            if(schema.AuxiliaryLastPurchase is not null&&ParseDate(reader.GetValue(column++)) is {} buy&&(!purchase.TryGetValue(code,out var priorBuy)||buy>priorBuy))purchase[code]=buy;
            if(schema.AuxiliaryLastSale is not null&&ParseDate(reader.GetValue(column++)) is {} sold&&(!sale.TryGetValue(code,out var priorSale)||sold>priorSale))sale[code]=sold;
            if(schema.AuxiliaryMinimumStock is not null){if(!reader.IsDBNull(column)&&decimal.TryParse(Convert.ToString(reader.GetValue(column),CultureInfo.InvariantCulture),NumberStyles.Number,CultureInfo.InvariantCulture,out var min))minimum[code]=min;column++;}
            if(schema.AuxiliarySupplierCode is not null&&!reader.IsDBNull(column))supplier[code]=Convert.ToString(reader.GetValue(column),CultureInfo.InvariantCulture)?.Trim()??"";
        }
        var providerNames = Lookup(connection, tx, schema.ProviderTable, schema.ProviderCode, schema.ProviderDescription, supplier.Values);
        var laboratoryNames = LookupLaboratories(connection, tx, schema, details.Values.Select(x => (x.Class, x.Laboratory)));
        var homogeneousNames = LookupHomogeneous(connection, tx, schema, codes);
        var barcodeValues = LookupBarcodes(connection, tx, schema, codes);
        return codes.Distinct().ToDictionary(c => c, c =>
        {
            // Nunca quedan existencias pendientes: un CN que no está dado de alta, sin fila de existencias o con stock
            // vacío tiene 0 unidades en la farmacia. El motivo se conserva como información.
            var issue = !descriptions.ContainsKey(c) ? "not_found" : !values.ContainsKey(c) ? "stock_not_found" : values[c] is null ? "stock_null" : issues.GetValueOrDefault(c);
            var detail = details.GetValueOrDefault(c); var supplierCode = supplier.GetValueOrDefault(c, "");
            return new StockArticle(c, descriptions.GetValueOrDefault(c, "Artículo no identificado"), issue == "not_found" ? 0 : values.GetValueOrDefault(c) ?? 0, issue)
            {
                Expiry = expiry.TryGetValue(c, out var e) ? e : null, LastPurchase = purchase.TryGetValue(c, out var b) ? b : null, LastSale = sale.TryGetValue(c, out var v) ? v : null,
                MinimumStock = minimum.TryGetValue(c, out var m) ? m : null, LastSupplier = providerNames.GetValueOrDefault(supplierCode),
                Laboratory = laboratoryNames.GetValueOrDefault((detail.Class, detail.Laboratory)), Classification = string.IsNullOrWhiteSpace(detail.Abc) ? null : detail.Abc.ToUpperInvariant(), Pvp = detail.Pvp,
                Homogeneous = homogeneousNames.GetValueOrDefault(c), Barcode = barcodeValues.GetValueOrDefault(c),
                Inactive = inactive.Contains(c), InactiveOn = inactiveOn.TryGetValue(c, out var off) ? off : null,
            };
        });
    }
    private static Dictionary<string, string> Lookup(DbConnection connection, DbTransaction tx, string? table, string? code, string? description, IEnumerable<string> requested)
    {
        var values = requested.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Take(10000).ToArray(); var result = new Dictionary<string, string>();
        if (table is null || code is null || description is null) return result;
        foreach (var batch in values.Chunk(250)) using (var cmd = Select(connection, tx, table, code, description, batch)) using (var reader = cmd.ExecuteReader()) while (reader.Read())
        {
            var key = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? ""; var value = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (key.Length > 0 && value.Length > 0) { if (result.TryGetValue(key, out var previous) && previous != value) result.Remove(key); else result.TryAdd(key, value); }
        }
        return result;
    }
    private static Dictionary<(string Class, string Code), string> LookupLaboratories(DbConnection connection, DbTransaction tx, Schema schema, IEnumerable<(string Class, string Code)> requested)
    {
        var keys = requested.Where(x => !string.IsNullOrWhiteSpace(x.Code)).Distinct().Take(10000).ToArray(); var result = new Dictionary<(string, string), string>();
        if (schema.LaboratoryTable is null || schema.LaboratoryKey is null || schema.LaboratoryClass is null || schema.LaboratoryDescription is null) return result;
        foreach (var batch in keys.Select(x => x.Code).Distinct().Chunk(250)) using (var cmd = Select(connection, tx, schema.LaboratoryTable, schema.LaboratoryKey, schema.LaboratoryDescription, batch, [schema.LaboratoryClass])) using (var reader = cmd.ExecuteReader()) while (reader.Read())
        {
            var code = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? ""; var description = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? ""; var @class = reader.IsDBNull(2) ? "" : Convert.ToString(reader.GetValue(2), CultureInfo.InvariantCulture)?.Trim() ?? "";
            if (code.Length > 0 && description.Length > 0) result.TryAdd((@class, code), description);
        }
        return result;
    }
    private static Dictionary<string, string> LookupHomogeneous(DbConnection connection, DbTransaction tx, Schema schema, IEnumerable<string> requested)
    {
        var result = new Dictionary<string, string>();
        if (new[] { schema.HomogeneousDetailTable, schema.HomogeneousArticleCode, schema.HomogeneousCommunity, schema.HomogeneousGroupCode, schema.HomogeneousMasterTable, schema.HomogeneousMasterCode, schema.HomogeneousMasterCommunity, schema.HomogeneousDescription }.Any(x => x is null)) return result;
        var articleGroups = new Dictionary<string, string>();
        foreach (var batch in requested.Distinct().Chunk(250))
        {
            var sql = $"SELECT {schema.HomogeneousArticleCode}, {schema.HomogeneousGroupCode} FROM {schema.HomogeneousDetailTable} WHERE {schema.HomogeneousCommunity}=? AND {schema.HomogeneousArticleCode} IN ({string.Join(",", batch.Select(_ => "?"))})";
            using var cmd = Command(connection, tx, sql); Add(cmd, "AND"); foreach (var code in batch) Add(cmd, code); using var reader = cmd.ExecuteReader();
            while (reader.Read()) { var article = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? ""; var group = Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? ""; if (TenenciaRules.ValidCode(article) && group.Length is > 0 and <= 100) articleGroups.TryAdd(article, group); }
        }
        var names = new Dictionary<string, string>();
        foreach (var batch in articleGroups.Values.Distinct().Chunk(250))
        {
            var sql = $"SELECT {schema.HomogeneousMasterCode}, {schema.HomogeneousDescription} FROM {schema.HomogeneousMasterTable} WHERE {schema.HomogeneousMasterCommunity}=? AND {schema.HomogeneousMasterCode} IN ({string.Join(",", batch.Select(_ => "?"))})";
            using var cmd = Command(connection, tx, sql); Add(cmd, "AND"); foreach (var code in batch) Add(cmd, code); using var reader = cmd.ExecuteReader();
            while (reader.Read()) { var code = Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)?.Trim() ?? ""; var name = reader.IsDBNull(1) ? "" : Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture)?.Trim() ?? ""; if (code.Length > 0 && name.Length > 0) names.TryAdd(code, name); }
        }
        foreach (var item in articleGroups) if (names.TryGetValue(item.Value, out var name)) result[item.Key] = name;
        return result;
    }
    private static Dictionary<string, string> LookupBarcodes(DbConnection connection, DbTransaction tx, Schema schema, IEnumerable<string> requested)
    {
        var codes = requested.Distinct().ToArray(); var result = Lookup(connection, tx, schema.BarcodeTable, schema.BarcodeArticleCode, schema.BarcodeValue, codes);
        var missing = codes.Where(c => !result.ContainsKey(c)).ToArray(); if (missing.Length == 0) return result;
        foreach (var item in Lookup(connection, tx, schema.RelativeBarcodeTable, schema.RelativeBarcodeArticleCode, schema.RelativeBarcodeValue, missing)) result.TryAdd(item.Key, item.Value);
        return result;
    }
    private static void Add(DbCommand command, string value) { var p = command.CreateParameter(); p.DbType = DbType.String; p.Value = value; command.Parameters.Add(p); }
    internal static string Quote(string identifier) { if (!Regex.IsMatch(identifier, "^[A-Za-z][A-Za-z0-9_ ]{0,127}$")) throw new InvalidDataException(); return "\"" + identifier + "\""; }
    private static string Code(object value) { var s = Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? ""; if (!TenenciaRules.ValidCode(s)) throw new InvalidDataException("Código nacional no reconocido."); return s; }
    private static DbCommand Select(DbConnection connection, DbTransaction tx, string table, string code, string value, string[] codes, string[]? extras = null)
    {
        extras ??= [];
        // Identifiers originate exclusively in Inspect; values always use positional ODBC parameters.
        foreach (var identifier in new[] { code, value }.Concat(extras)) if (!Regex.IsMatch(identifier, "^\"[A-Za-z][A-Za-z0-9_ ]{0,127}\"$")) throw new ArgumentException();
        if (table is not ("ARTICULOS" or "EXISTENCIAS" or "APADMIN_AB_ARTICULOS" or "APADMIN.AB_ARTICULOS" or "APADMIN_AC_EXISTENCIAS" or "APADMIN.AC_EXISTENCIAS" or "AC_EXISTENCIAS" or "AC1_EXISTENCIAS" or "APPUL.AB_ARTICULOS" or "APPUL.AB_ARTICULOS_FICHA_E" or "APADMIN_AB_ARTICULOS_FICHA_E" or "APADMIN.AB_ARTICULOS_FICHA_E" or "APADMIN_AD_PROVEEDORES" or "APADMIN.AD_PROVEEDORES" or "DW_AB_LABORATORIOS" or "DW.AB_LABORATORIOS" or "DSP_AB_ARTICULOS" or "DSP.AB_ARTICULOS" or "DONET_AD_CRELATIVOS" or "DONET.AD_CRELATIVOS")) throw new ArgumentException();
        var cmd = Command(connection, tx, $"SELECT {string.Join(", ", new[] { code, value }.Concat(extras))} FROM {table} WHERE {code} IN ({string.Join(",", codes.Select(_ => "?"))})"); foreach (var c in codes) { var p = cmd.CreateParameter(); p.DbType = DbType.String; p.Value = c; cmd.Parameters.Add(p); }
        return cmd;
    }
    internal static DbCommand Command(DbConnection connection, DbTransaction tx, string text) { var cmd = connection.CreateCommand(); cmd.Transaction = tx; cmd.CommandTimeout = 15; cmd.CommandText = text; return cmd; }
}
