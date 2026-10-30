using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

/// <summary>
/// Listado de tenencia editable por Viteka (superadministrador). Las modificaciones se guardan en el servidor como
/// una copia completa de la lista provincial; la lista empaquetada se conserva para poder restaurarla.
/// También guarda la última consulta de existencias para mostrarla al volver a abrir Tenencia.
/// </summary>
public sealed class TenenciaCatalogStore(string directory, string packagedDirectory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly Lock gate = new();
    private string CustomPath(string province) => Path.Combine(directory, "tenencia-" + province + ".json");
    private string LastPath(string province) => Path.Combine(directory, "tenencia-ultima-" + province + ".json");

    public bool IsCustomized(string province) => File.Exists(CustomPath(province));

    public TenenciaList? Load(string province)
    {
        if (!SpanishProvinces.Items.ContainsKey(province)) throw new ArgumentException("Provincia no válida.");
        var custom = CustomPath(province);
        if (File.Exists(custom))
        {
            if (new FileInfo(custom).Length > 4 * 1024 * 1024) throw new InvalidDataException("La lista de tenencia excede el tamaño permitido.");
            var list = JsonSerializer.Deserialize<TenenciaList>(File.ReadAllBytes(custom), Json) ?? throw new InvalidDataException();
            TenenciaRules.Validate(list);
            if (list.Province != province) throw new InvalidDataException("La lista no corresponde a la provincia.");
            return list;
        }
        return TenenciaRules.Load(packagedDirectory, province);
    }

    /// <summary>Aplica una operación de edición y devuelve la lista resultante.</summary>
    public TenenciaList Apply(string province, string action, JsonElement data)
    {
        lock (gate)
        {
            if (action == "reset") { File.Delete(CustomPath(province)); File.Delete(LastPath(province)); return Load(province) ?? throw new InvalidDataException("No hay lista empaquetada para esta provincia."); }
            var list = Load(province) ?? NewList(province);
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
            var edited = list with { Version = "Personalizada-" + now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture), Source = source, ReviewedOn = DateOnly.FromDateTime(DateTime.UtcNow), Groups = groups.ToArray() };
            try { TenenciaRules.Validate(edited); }
            catch (InvalidDataException error) { throw new ArgumentException(error.Message, error); }
            Write(CustomPath(province), JsonSerializer.SerializeToUtf8Bytes(edited, Json));
            return edited;
        }
    }
    private static TenenciaList NewList(string province) =>
        new(1, province, "Personalizada", "Listado creado en Viteka.", DateOnly.FromDateTime(DateTime.UtcNow), []);

    public void SaveLast(string province, TenenciaResponse response)
    {
        if (response.Code != "ok" || response.Rows is null) return;
        try { lock (gate) Write(LastPath(province), JsonSerializer.SerializeToUtf8Bytes(response, Json)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* sin memoria de la última consulta */ }
    }
    public TenenciaResponse? LoadLast(string province, string listVersion)
    {
        try
        {
            var path = LastPath(province);
            if (!File.Exists(path) || new FileInfo(path).Length > 16 * 1024 * 1024) return null;
            var last = JsonSerializer.Deserialize<TenenciaResponse>(File.ReadAllBytes(path), Json);
            return last is { Code: "ok", Rows: not null } && last.Province == province && last.ListVersion == listVersion ? last : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
    private void Write(string path, byte[] content)
    {
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, content); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
