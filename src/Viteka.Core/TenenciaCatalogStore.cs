using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

/// <summary>
/// Listado de tenencia editable por Viteka (superadministrador). Las modificaciones se guardan en el servidor como
/// una revisión transaccional de base/vista local; se conservan las fuentes JSON para migración.
/// También guarda la última consulta de existencias para mostrarla al volver a abrir Tenencia.
/// </summary>
public sealed class TenenciaCatalogStore(string directory, string packagedDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock gate = new();
    public PharmacyDatabase Database { get; } = new(directory);
    public CatalogRevisions Revisions => new(Database);
    private string CustomPath(string province) => Path.Combine(directory, "tenencia-" + province + ".json");
    private string LastPath(string province) => Path.Combine(directory, "tenencia-ultima-" + province + ".json");

    public bool IsCustomized(string province) { var s = Revisions.Read(province); return s is null ? File.Exists(CustomPath(province)) : s.Legacy || !System.Text.Json.Nodes.JsonNode.DeepEquals(JsonSerializer.SerializeToNode(s.Base.Groups,Json),JsonSerializer.SerializeToNode(s.Effective.Groups,Json)); }

    public TenenciaList? Load(string province)
    {
        if (!SpanishProvinces.Items.ContainsKey(province)) throw new ArgumentException("Provincia no válida.");
        if (Revisions.Read(province) is {} current) return current.Effective;
        var packaged = TenenciaRules.Load(packagedDirectory, province);
        var custom = CustomPath(province);
        if (File.Exists(custom))
        {
            if (new FileInfo(custom).Length > 4 * 1024 * 1024) throw new InvalidDataException("La lista de tenencia excede el tamaño permitido.");
            var list = JsonSerializer.Deserialize<TenenciaList>(File.ReadAllBytes(custom), Json) ?? throw new InvalidDataException();
            TenenciaRules.Validate(list);
            if (list.Province != province) throw new InvalidDataException("La lista no corresponde a la provincia.");
            if (packaged is not null) return Revisions.Ensure(packaged, list).Effective;
            return Revisions.Ensure(list, list).Effective;
        }
        return packaged is null ? null : Revisions.Ensure(packaged).Effective;
    }

    /// <summary>Aplica una operación de edición y devuelve la lista resultante.</summary>
    public TenenciaList Apply(string province, string action, JsonElement data, AuditActor? actor = null)
    {
        lock (gate)
        {
            var list = Load(province) ?? NewList(province);
            var expected = data.TryGetProperty("expectedVersion",out var v) ? v.GetString() : list.Version;
            if (expected != list.Version) throw new ArgumentException("El listado cambió en otro puesto. Recarga antes de guardar.");
            actor ??= new("system","Sistema");
            if (action == "reset") return Revisions.Reset(province,list.Version,actor);
            var groups = list.Groups.ToList();
            TenenciaGroup Find(string id) => groups.FirstOrDefault(g => g.Id == id) ?? throw new KeyNotFoundException();
            string Text(string name, int max, bool required = true)
            {
                var value = data.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : "";
                if (required && value.Length == 0 || value.Length > max || value.Any(char.IsControl)) throw new ArgumentException("Revisa el campo " + name + ".");
                return value;
            }
            switch (action)
            {
                case "group.save":
                    {
                        var id = data.TryGetProperty("id", out var idValue) && idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                        var name = Text("name", 200);
                        var code = Text("code", 12, false).ToUpperInvariant();
                        var note = Text("note", 600, false);
                        if (!data.TryGetProperty("minimum", out var minValue) || !minValue.TryGetInt32(out var minimum) || minimum is < 1 or > 10000) throw new ArgumentException("El mínimo debe estar entre 1 y 10.000 unidades.");
                        if (code.Length > 0 && !TenenciaRules.ValidAtc(code) && !Regex.IsMatch(code, "^[A-Z0-9]{2,12}$", RegexOptions.CultureInvariant)) throw new ArgumentException("El código solo admite letras y números (máximo 12).");
                        string? atc = code.Length > 0 && TenenciaRules.ValidAtc(code) ? code : null;
                        string? classification = code.Length > 0 && atc is null ? code : null;
                        if (id is null)
                        {
                            if (groups.Count >= 250) throw new ArgumentException("Máximo 250 grupos.");
                            var next = new TenenciaGroup("custom-" + Guid.NewGuid().ToString("N")[..10], name, minimum, []) { AtcPrefix = atc, ClassificationCode = classification, SelectionNote = note.Length > 0 ? note : null };
                            var after = data.TryGetProperty("afterId", out var a) && a.ValueKind == JsonValueKind.String ? groups.FindIndex(g => g.Id == a.GetString()) : -1;
                            groups.Insert(after < 0 ? groups.Count : after + 1, next);
                        }
                        else
                        {
                            var current = Find(id); var index = groups.IndexOf(current);
                            groups[index] = current with { Name = name, Minimum = minimum, AtcPrefix = atc, ClassificationCode = classification, SelectionNote = note.Length > 0 ? note : null };
                        }
                        break;
                    }
                case "group.delete":
                    {
                        if (groups.Count == 1) throw new ArgumentException("La lista debe conservar al menos un grupo.");
                        groups.Remove(Find(Text("id", 80)));
                        break;
                    }
                case "codes.add":
                    {
                        var group = Find(Text("id", 80));
                        if (!data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() is < 1 or > 1000) throw new ArgumentException("Indica entre 1 y 1.000 artículos.");
                        var codes = group.Codes.ToList(); var descriptions = new Dictionary<string, string>(group.Descriptions ?? []);
                        foreach (var item in items.EnumerateArray())
                        {
                            var code = item.TryGetProperty("code", out var c) ? c.GetString()?.Trim() : null;
                            if (!TenenciaRules.ValidCode(code)) throw new ArgumentException("Código nacional no válido: " + code);
                            var description = item.TryGetProperty("description", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString()!.Trim() : "";
                            if (description.Length > 200 || description.Any(char.IsControl)) description = description[..Math.Min(200, description.Length)].Replace("\n", " ").Replace("\r", " ");
                            if (!codes.Contains(code!)) codes.Add(code!);
                            if (description.Length > 0) descriptions[code!] = description;
                        }
                        if (codes.Count > 1000) throw new ArgumentException("Máximo 1.000 artículos por grupo.");
                        groups[groups.IndexOf(group)] = group with { Codes = codes.ToArray(), Descriptions = descriptions };
                        break;
                    }
                case "codes.remove":
                    {
                        var group = Find(Text("id", 80));
                        if (!data.TryGetProperty("codes", out var items) || items.ValueKind != JsonValueKind.Array) throw new ArgumentException("Indica los códigos que quieres quitar.");
                        var remove = items.EnumerateArray().Select(i => i.GetString()).ToHashSet();
                        var descriptions = (group.Descriptions ?? []).Where(p => !remove.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
                        groups[groups.IndexOf(group)] = group with { Codes = group.Codes.Where(c => !remove.Contains(c)).ToArray(), Descriptions = descriptions };
                        break;
                    }
                default: throw new ArgumentException("Operación de listado no válida.");
            }
            var now = DateTime.Now;
            var source = list.Source.Contains("Editada en Viteka", StringComparison.Ordinal) ? list.Source : (list.Source.Length > 440 ? list.Source[..440] : list.Source) + " · Editada en Viteka.";
            var edited = list with { Version = CatalogRevisions.NewVersion(), Source = source, ReviewedOn = DateOnly.FromDateTime(DateTime.UtcNow), Groups = groups.ToArray() };
            try { TenenciaRules.Validate(edited); }
            catch (InvalidDataException error) { throw new ArgumentException(error.Message, error); }
            var first=Revisions.Read(province) is null;
            if (first) Revisions.Ensure(edited);
            return Revisions.SaveLocal(edited, first?edited.Version:list.Version, actor, "catalog."+action);
        }
    }
    private static TenenciaList NewList(string province) =>
        new(1, province, "Personalizada", "Listado creado en Viteka.", DateOnly.FromDateTime(DateTime.UtcNow), []);

    public void SaveLast(string province, TenenciaResponse response)
    {
        if(response.Code!="ok"||response.Rows is null)return;
        Database.Transaction((c,tx)=>{
            var state=System.Text.Json.JsonSerializer.Deserialize<CatalogRevision>(PharmacyDatabase.Get(c,tx,"catalog."+province)??"null",Json);
            if(state?.Effective.Version==response.ListVersion)PharmacyDatabase.Set(c,tx,"last."+province,response);
            return true;
        });
    }
    public TenenciaResponse? LoadLast(string province,string listVersion)
    {
        return Database.Transaction((c,tx)=>{
            var catalog=System.Text.Json.JsonSerializer.Deserialize<CatalogRevision>(PharmacyDatabase.Get(c,tx,"catalog."+province)??"null",Json);
            if(catalog?.Effective.Version!=listVersion)return null;
            var json=PharmacyDatabase.Get(c,tx,"last."+province);
            if(json is null && File.Exists(LastPath(province)) && new FileInfo(LastPath(province)).Length<=16*1024*1024)json=File.ReadAllText(LastPath(province));
            if(json is null)return null;
            try{var last=JsonSerializer.Deserialize<TenenciaResponse>(json,Json);return last is {Code:"ok",Rows:not null} && last.Province==province && last.ListVersion==listVersion?last:null;}
            catch(JsonException){return null;}
        });
    }
    private void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
