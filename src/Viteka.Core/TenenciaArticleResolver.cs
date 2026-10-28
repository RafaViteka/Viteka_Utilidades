using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class TenenciaArticleResolver
{
    public sealed record Resolution(Dictionary<string,string[]> Groups,Dictionary<string,string> Notes,string? AtcColumn);
    public static Resolution Resolve(DbConnection connection,DbTransaction tx,TenenciaStockQuery.Schema schema,TenenciaList list,CancellationToken ct)
    {
        TenenciaRules.Validate(list);
        var identifiers=new[]{schema.ArticleCode,schema.Description}.Concat(schema.Atc is null?[]:new[]{schema.Atc}).Concat(schema.AtcCandidates);
        if(identifiers.Any(x=>!Regex.IsMatch(x,"^\"[A-Za-z][A-Za-z0-9_ ]{0,127}\"$")))throw new InvalidDataException("Nombre de columna no válido.");
        var groups=list.Groups.ToDictionary(g=>g.Id,g=>g.Codes);var notes=new Dictionary<string,string>();
        var requested=list.Groups.Where(g=>g.Codes.Length==0&&g.AtcPrefix is not null).ToArray();
        var column=schema.Atc;
        if(requested.Length>0&&column is null){
            if(schema.AtcCandidates.Length<=20){
                var matches=new List<string>();
                foreach(var candidate in schema.AtcCandidates){
                    ct.ThrowIfCancellationRequested();using var cmd=Command(connection,tx,$"SELECT DISTINCT {candidate} FROM ARTICULOS WHERE {candidate} IS NOT NULL AND ROWNUM <= 201");using var reader=cmd.ExecuteReader();var values=new HashSet<string>();var scanned=0;
                    while(reader.Read()){
                        ct.ThrowIfCancellationRequested();if(++scanned>201)throw new InvalidDataException("La muestra de clasificación excede el límite.");
                        var value=Convert.ToString(reader.GetValue(0),CultureInfo.InvariantCulture)?.Trim().ToUpperInvariant()??"";
                        if(TenenciaRules.ValidAtc(value)&&requested.Any(g=>value.StartsWith(g.AtcPrefix!,StringComparison.Ordinal)))values.Add(value);
                    }
                    if(values.Count>=2)matches.Add(candidate);
                }
                if(matches.Count==1)column=matches[0];
            }
        }
        foreach(var g in list.Groups.Where(g=>g.Codes.Length==0&&g.ClassificationCode is not null))notes[g.Id]="La clasificación de productos sanitarios no se interpreta como ATC. Falta enlazar sus códigos nacionales.";
        if(requested.Length==0)return new(groups,notes,column);
        if(column is null){foreach(var g in requested)notes[g.Id]="No se ha identificado una columna ATC inequívoca en ARTICULOS. Viteka debe revisar el diagnóstico de columnas y coincidencias.";return new(groups,notes,null);}
        var prefixes=requested.Select(g=>g.AtcPrefix!).Distinct().ToArray();
        // Every identifier is from trusted metadata; ATC values come from the packaged, validated list.
        var predicates=string.Join(" OR ",prefixes.Select(_=>$"UPPER(TRIM({column})) LIKE ?"));
        using(var cmd=Command(connection,tx,$"SELECT {schema.ArticleCode}, {schema.Description}, {column} FROM ARTICULOS WHERE ({predicates}) AND ROWNUM <= 10001")){
            foreach(var prefix in prefixes){var parameter=cmd.CreateParameter();parameter.DbType=DbType.String;parameter.Value=prefix+"%";cmd.Parameters.Add(parameter);}
            using var reader=cmd.ExecuteReader();var entries=new Dictionary<string,HashSet<string>>();var count=0;
            while(reader.Read()){
                ct.ThrowIfCancellationRequested();if(++count>10000)throw new InvalidDataException("Más de10000 coincidencias ATC: hay que acotar la lectura, no se ha evaluado parcialmente.");
                var code=Convert.ToString(reader.GetValue(0),CultureInfo.InvariantCulture)?.Trim()??"";
                var atc=Convert.ToString(reader.GetValue(2),CultureInfo.InvariantCulture)?.Trim().ToUpperInvariant()??"";
                if(!TenenciaRules.ValidCode(code)||!TenenciaRules.ValidAtc(atc))throw new InvalidDataException("Artículo o clasificación ATC no reconocidos.");
                if(!entries.TryGetValue(atc,out var codes))entries[atc]=codes=[];codes.Add(code);
            }
            foreach(var g in requested){groups[g.Id]=entries.Where(e=>e.Key.StartsWith(g.AtcPrefix!,StringComparison.Ordinal)).SelectMany(e=>e.Value).Distinct().Order().ToArray();notes[g.Id]=groups[g.Id].Length==0?"No se han encontrado artículos coincidentes con este ATC; no equivale a stock cero.":"Coincidencias ATC obtenidas de ARTICULOS mediante "+column+".";}
        }
        return new(groups,notes,column);
    }
    private static DbCommand Command(DbConnection c,DbTransaction tx,string text){var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=text;cmd.CommandTimeout=15;return cmd;}
}
