using System.Data.Common;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Viteka.Contracts;
namespace Viteka.Core;

/// <summary>Conditional Nixfarma adapter: no relation is used until metadata and a matching join are observed.</summary>
public static class TherapeuticFinder
{
    public sealed record Selection(string Code,string Class,string Type,bool Descendants);
    private const string Groups="APPUL.AB_GRUPOS_TERAPEUTICOS";
    private static bool Available(DbConnection c,DbTransaction tx,TenenciaStockQuery.Schema schema)
    {
        if(schema.ArticleTable!="APPUL.AB_ARTICULOS"||!new[]{"GTE_CODIGO","CLASE","GTETIP"}.All(n=>schema.ArticleColumns.Contains(n,StringComparer.OrdinalIgnoreCase)))return false;
        using(var cmd=TenenciaStockQuery.Command(c,tx,"SELECT * FROM "+Groups+" WHERE 1=0"))using(var r=cmd.ExecuteReader()){
            var fields=Enumerable.Range(0,r.FieldCount).Select(r.GetName).ToArray();if(!new[]{"CODIGO","DESCRIPCION","CLASE","GTETIP","GTENIV","GTEPAD"}.All(n=>fields.Contains(n,StringComparer.OrdinalIgnoreCase)))return false;
        }
        using var sample=TenenciaStockQuery.Command(c,tx,"SELECT a.CODIGO FROM APPUL.AB_ARTICULOS a JOIN "+Groups+" g ON g.CODIGO=a.GTE_CODIGO AND g.CLASE=a.CLASE AND g.GTETIP=a.GTETIP WHERE ROWNUM<=1");using var read=sample.ExecuteReader();return read.Read();
    }
    public static ArticleSearchResult Search(DbConnection c,DbTransaction tx,TenenciaStockQuery.Schema schema,string mode,string text,string? field,CancellationToken ct)
    {
        if(!Available(c,tx,schema))return new("unavailable","No se ha verificado el enlace terapéutico por código, clase y tipo en esta instalación. No se utilizan agrupaciones homogéneas como sustituto.",[]);
        if(mode=="therapeutic.groups"){
            text=text.Trim().ToUpperInvariant();if(text.Length is <2 or >80)return new("empty","Escribe al menos dos caracteres de código o descripción.",[]);
            using var cmd=TenenciaStockQuery.Command(c,tx,"SELECT CODIGO,DESCRIPCION,CLASE,GTETIP,GTENIV,GTEPAD FROM "+Groups+" WHERE (UPPER(CODIGO) LIKE ? OR UPPER(DESCRIPCION) LIKE ?) AND ROWNUM<=201");
            Add(cmd,"%"+text.Replace("%","").Replace("_","")+"%","%"+text.Replace("%","").Replace("_","")+"%");
            var groups=new List<TherapeuticClassification>();using var r=cmd.ExecuteReader();while(r.Read()){
                ct.ThrowIfCancellationRequested();if(groups.Count==200)break;
                string S(int i)=>r.IsDBNull(i)?"":Convert.ToString(r.GetValue(i),CultureInfo.InvariantCulture)!.Trim();
                var level=int.TryParse(S(4),out var l)?l:(int?)null;
                groups.Add(new(S(0),S(1),S(2),S(3),level,S(5),"Nixfarma · AB_GRUPOS_TERAPEUTICOS"));
            }
            return new("ok","Clasificaciones encontradas. El nivel y el padre proceden de Nixfarma; acota la búsqueda si hay 200 resultados.",[],groups.Count==200){Classifications=groups.ToArray()};
        }
        if(mode!="therapeutic.articles"||field is null||field.Length>1200)throw new ArgumentException("Selección terapéutica no válida.");
        var selected=JsonSerializer.Deserialize<Selection>(field,PharmacyDatabase.Json)??throw new ArgumentException();
        if(new[]{selected.Code,selected.Class,selected.Type}.Any(v=>string.IsNullOrWhiteSpace(v)||v.Length>40||v.Any(char.IsControl)))throw new ArgumentException();
        using(var unique=TenenciaStockQuery.Command(c,tx,"SELECT CODIGO FROM "+Groups+" WHERE CODIGO=? AND CLASE=? AND GTETIP=? AND ROWNUM<=2")){
            Add(unique,selected.Code,selected.Class,selected.Type);using var r=unique.ExecuteReader();if(!r.Read()||r.Read())throw new InvalidDataException("La clasificación no es única por código, clase y tipo.");
        }
        var codes=new List<string>{selected.Code};
        if(selected.Descendants){
            using var descendants=TenenciaStockQuery.Command(c,tx,"SELECT CODIGO FROM "+Groups+" WHERE ROWNUM<=1001 START WITH CODIGO=? AND CLASE=? AND GTETIP=? CONNECT BY NOCYCLE PRIOR CODIGO=GTEPAD AND PRIOR CLASE=CLASE AND PRIOR GTETIP=GTETIP");
            Add(descendants,selected.Code,selected.Class,selected.Type);using var r=descendants.ExecuteReader();while(r.Read()){
                ct.ThrowIfCancellationRequested();if(codes.Count>1000)throw new InvalidDataException("Demasiados descendientes; selecciona un nivel más concreto.");var value=Convert.ToString(r.GetValue(0),CultureInfo.InvariantCulture)??"";if(!codes.Contains(value))codes.Add(value);
            }
        }
        if(codes.Count>1000)throw new InvalidDataException("Demasiados descendientes; selecciona un nivel más concreto.");
        var marks=string.Join(",",codes.Select(_=>"?"));using var articles=TenenciaStockQuery.Command(c,tx,"SELECT CODIGO FROM (SELECT DISTINCT CODIGO FROM APPUL.AB_ARTICULOS WHERE GTE_CODIGO IN ("+marks+") AND CLASE=? AND GTETIP=? ORDER BY CODIGO) WHERE ROWNUM<=201");
        Add(articles,[..codes,selected.Class,selected.Type]);var national=new List<string>();using(var r=articles.ExecuteReader())while(r.Read()){
            ct.ThrowIfCancellationRequested();var code=Convert.ToString(r.GetValue(0),CultureInfo.InvariantCulture)?.Trim();if(TenenciaRules.ValidCode(code)&&!national.Contains(code!))national.Add(code!);
        }
        var truncated=national.Count>200;national=national.Take(200).ToList();var found=national.Count>0?TenenciaStockQuery.Read(c,tx,schema,national.ToArray(),ct).Values.ToArray():[];
        return new("ok",found.Length+" artículos · "+selected.Code+" · "+(selected.Descendants?"incluye descendientes":"solo grupo seleccionado")+(truncated?" · resultado limitado; acota el grupo":""),found,truncated){Group=selected.Code};
    }
    private static void Add(DbCommand cmd,params string[] values){foreach(var value in values){var p=cmd.CreateParameter();p.DbType=DbType.String;p.Value=value;cmd.Parameters.Add(p);}}
}
