using System.Text.Json;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class TenenciaRules
{
    public static TenenciaList? Load(string directory,string province)
    {
        if(!SpanishProvinces.Items.ContainsKey(province))throw new ArgumentException("Provincia no válida.");
        var file=Path.Combine(directory,province+".json");if(!File.Exists(file))return null;
        if(new FileInfo(file).Length>524288)throw new InvalidDataException("La lista de tenencia excede el tamaño permitido.");
        var list=JsonSerializer.Deserialize<TenenciaList>(File.ReadAllBytes(file),new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw new InvalidDataException();
        Validate(list);if(list.Province!=province)throw new InvalidDataException("La lista no corresponde a la provincia.");return list;
    }
    public static void Validate(TenenciaList list)
    {
        if(list is null||list.Schema!=1||!SpanishProvinces.Items.ContainsKey(list.Province??"")||string.IsNullOrWhiteSpace(list.Version)||list.Version.Length>100||string.IsNullOrWhiteSpace(list.Source)||list.Source.Length>500||list.ReviewedOn==default||list.ReviewedOn>DateOnly.FromDateTime(DateTime.UtcNow)||list.Groups is null||list.Groups.Length is <1 or >250)throw new InvalidDataException("La lista de tenencia no es válida.");
        if(list.Groups.Any(g=>g is null||string.IsNullOrWhiteSpace(g.Id)||g.Id.Length>80||string.IsNullOrWhiteSpace(g.Name)||g.Name.Length>200||g.Minimum is <1 or >10000||g.Codes is null||g.Codes.Length is <1 or >100||g.Codes.Any(c=>!ValidCode(c))||g.Codes.Distinct().Count()!=g.Codes.Length||g.Descriptions is not null&&g.Descriptions.Any(p=>!g.Codes.Contains(p.Key)||string.IsNullOrWhiteSpace(p.Value)||p.Value.Length>200))||list.Groups.Select(g=>g.Id).Distinct().Count()!=list.Groups.Length||list.Groups.SelectMany(g=>g.Codes).Distinct().Count()>1000)throw new InvalidDataException("Revisa los grupos, códigos y unidades de la lista.");
    }
    public static bool ValidCode(string? code)=>code is not null&&Regex.IsMatch(code,"^[0-9]{6,7}$",RegexOptions.CultureInvariant);
    public static TenenciaRow[] Preview(TenenciaList list)
    {
        Validate(list);
        return list.Groups.Select(g=>new TenenciaRow(g.Id,g.Name,g.Minimum,null,null,"not_checked",g.Codes.Select(c=>new StockArticle(c,g.Descriptions?.GetValueOrDefault(c)??("Artículo CN "+c),null,"not_checked")).ToArray())).ToArray();
    }
    public static TenenciaRow[] Evaluate(TenenciaList list,IReadOnlyDictionary<string,StockArticle> stocks)
    {
        Validate(list);
        return list.Groups.Select(g=>{
            var articles=g.Codes.Select(c=>stocks.TryGetValue(c,out var a)?a with{Description=a.Description=="Artículo no identificado"?g.Descriptions?.GetValueOrDefault(c)??a.Description:a.Description}:new StockArticle(c,g.Descriptions?.GetValueOrDefault(c)??"Artículo no identificado",null,"not_found")).ToArray();
            decimal? total=articles.All(a=>a.Stock is not null&&a.Issue is null)?articles.Sum(a=>a.Stock!.Value):null;
            return new TenenciaRow(g.Id,g.Name,g.Minimum,total,total is {} value?Math.Max(0,g.Minimum-value):null,total is null?"unresolved":total>=g.Minimum?"covered":"shortfall",articles);
        }).ToArray();
    }
}
