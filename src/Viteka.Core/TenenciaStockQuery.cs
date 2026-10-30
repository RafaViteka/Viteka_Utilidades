using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class TenenciaStockQuery
{
    public sealed record Schema(string ArticleCode,string Description,string StockCode,string Stock) { public string ArticleTable {get;init;}="ARTICULOS"; public string StockTable {get;init;}="EXISTENCIAS"; public string? Atc {get;init;} public string[] ArticleColumns {get;init;}=[]; public string[] AtcCandidates {get;init;}=[]; }
    public static Schema Inspect(DbConnection connection,DbTransaction transaction,bool nativeNixFarma=false)
    {
        string[] Columns(string table){using var cmd=Command(connection,transaction,"SELECT * FROM "+table+" WHERE 1 = 0");using var reader=cmd.ExecuteReader();return Enumerable.Range(0,reader.FieldCount).Select(reader.GetName).ToArray();}
        string Find(string[] columns,params string[] names){var hits=columns.Where(c=>names.Contains(c.Replace("_","").Replace(" ","").ToUpperInvariant())).ToArray();if(hits.Length!=1||!Regex.IsMatch(hits[0],"^[A-Za-z][A-Za-z0-9_ ]{0,127}$"))throw new InvalidDataException("No se identifica de forma única la columna de artículos o stock. Viteka debe revisar la estructura.");return "\""+hits[0]+"\"";}
        var articleTable=nativeNixFarma?"APPUL.AB_ARTICULOS":"ARTICULOS";var stockTable=nativeNixFarma?"APPUL.AB_ARTICULOS_FICHA_E":"EXISTENCIAS";
        var a=Columns(articleTable);var e=Columns(stockTable);
        var candidates=a.Where(c=>Regex.IsMatch(c,"^[A-Za-z][A-Za-z0-9_ ]{0,127}$")&&Regex.IsMatch(c,"ATC|TERAP|GRUPO",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)).ToArray();
        var exact=candidates.Where(c=>new[]{"ATC","CODIGOATC","GRUPOATC"}.Contains(c.Replace("_","").Replace(" ","").ToUpperInvariant())).ToArray();
        return new(Find(a,nativeNixFarma?"CODIGO":"ARTICULO"),Find(a,"DESCRIPCION"),Find(e,nativeNixFarma?"ARTCODIGO":"ARTICULO"),Find(e,nativeNixFarma?["EXIACTUALES"]:["ACTUAL","STOCKACTUAL"])){ArticleTable=articleTable,StockTable=stockTable,Atc=exact.Length==1?Quote(exact[0]):null,ArticleColumns=a,AtcCandidates=candidates.Select(Quote).ToArray()};
    }
    public static T ReadOnly<T>(DbConnection connection,Func<DbTransaction,T> action)
    {
        connection.Open();using var tx=connection.BeginTransaction();try{using var readOnly=Command(connection,tx,"SET TRANSACTION READ ONLY");readOnly.ExecuteNonQuery();return action(tx);}finally{try{tx.Rollback();}catch(DbException){}}
    }
    public static Dictionary<string,StockArticle> Read(DbConnection connection,DbTransaction tx,Schema schema,string[] codes,CancellationToken ct=default)
    {
        if(codes.Length is <1 or >10000||codes.Any(c=>!TenenciaRules.ValidCode(c)))throw new ArgumentException("Códigos no válidos.");
        var descriptions=new Dictionary<string,string>();var values=new Dictionary<string,decimal?>();var issues=new Dictionary<string,string>();var count=0;
        foreach(var batch in codes.Distinct().Chunk(250))
        {
            ct.ThrowIfCancellationRequested();
            using(var cmd=Select(connection,tx,schema.ArticleTable,schema.ArticleCode,schema.Description,batch))
            using(var reader=cmd.ExecuteReader())while(reader.Read()){
                ct.ThrowIfCancellationRequested();
                if(++count>100000)throw new InvalidDataException("Demasiadas filas: revisa el alcance de la consulta.");var code=Code(reader.GetValue(0));if(!batch.Contains(code))throw new InvalidDataException();
                if(!descriptions.TryAdd(code,reader.IsDBNull(1)?"Sin descripción":Convert.ToString(reader.GetValue(1),CultureInfo.InvariantCulture)![..Math.Min(200,Convert.ToString(reader.GetValue(1),CultureInfo.InvariantCulture)!.Length)]))issues[code]="duplicate_article";
            }
            using(var cmd=Select(connection,tx,schema.StockTable,schema.StockCode,schema.Stock,batch))
            using(var reader=cmd.ExecuteReader())while(reader.Read()){
                ct.ThrowIfCancellationRequested();
                if(++count>100000)throw new InvalidDataException("Demasiadas filas: revisa el alcance de la consulta.");var code=Code(reader.GetValue(0));if(!batch.Contains(code))throw new InvalidDataException();
                decimal? stock=reader.IsDBNull(1)?null:Convert.ToDecimal(reader.GetValue(1),CultureInfo.InvariantCulture);
                if(!values.TryAdd(code,stock))issues[code]="ambiguous_stock";
            }
        }
        return codes.Distinct().ToDictionary(c=>c,c=>{
            var issue=issues.GetValueOrDefault(c)??(!descriptions.ContainsKey(c)?"not_found":!values.ContainsKey(c)?"stock_not_found":values[c] is null?"stock_null":null);
            return new StockArticle(c,descriptions.GetValueOrDefault(c,"Artículo no identificado"),issue is null?values[c]:null,issue);
        });
    }
    internal static string Quote(string identifier){if(!Regex.IsMatch(identifier,"^[A-Za-z][A-Za-z0-9_ ]{0,127}$"))throw new InvalidDataException();return "\""+identifier+"\"";}
    private static string Code(object value){var s=Convert.ToString(value,CultureInfo.InvariantCulture)?.Trim()??"";if(!TenenciaRules.ValidCode(s))throw new InvalidDataException("Código nacional no reconocido.");return s;}
    private static DbCommand Select(DbConnection connection,DbTransaction tx,string table,string code,string value,string[] codes)
    {
        // Identifiers originate exclusively in Inspect; values always use positional ODBC parameters.
        foreach(var identifier in new[]{code,value})if(!Regex.IsMatch(identifier,"^\"[A-Za-z][A-Za-z0-9_ ]{0,127}\"$"))throw new ArgumentException();
        if(table is not ("ARTICULOS" or "EXISTENCIAS" or "APPUL.AB_ARTICULOS" or "APPUL.AB_ARTICULOS_FICHA_E"))throw new ArgumentException();
        var cmd=Command(connection,tx,$"SELECT {code}, {value} FROM {table} WHERE {code} IN ({string.Join(",",codes.Select(_=>"?"))})");foreach(var c in codes){var p=cmd.CreateParameter();p.DbType=DbType.String;p.Value=c;cmd.Parameters.Add(p);}return cmd;
    }
    private static DbCommand Command(DbConnection connection,DbTransaction tx,string text){var cmd=connection.CreateCommand();cmd.Transaction=tx;cmd.CommandTimeout=15;cmd.CommandText=text;return cmd;}
}
