using System.Security.Cryptography;
using System.Text.Json;
using Viteka.Core;
using Viteka.Contracts;
namespace Viteka.Server;
internal sealed class CatalogDistribution(string directory,TenenciaCatalogStore catalogs)
{
    private string PublicKey=>Path.Combine(directory,"catalog-trust.pem");
    private string PrivateKey=>Path.Combine(directory,"catalog-master.pem");
    public bool CanPrepare=>File.Exists(PrivateKey);
    public bool HasTrust=>File.Exists(PublicKey);
    public static void InitializeKey(string directory)
    {
        Directory.CreateDirectory(directory);using var key=RSA.Create(3072);
        using(var output=new FileStream(Path.Combine(directory,"catalog-master.pem"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){
            var bytes=System.Text.Encoding.UTF8.GetBytes(key.ExportPkcs8PrivateKeyPem());output.Write(bytes);output.Flush(true);
        }
        using var publicOutput=new StreamWriter(new FileStream(Path.Combine(directory,"catalog-trust.pem"),FileMode.CreateNew));publicOutput.Write(key.ExportSubjectPublicKeyInfoPem());
    }
    private RSA ReadKey(bool signing=false){var key=RSA.Create();try{key.ImportFromPem(File.ReadAllText(signing?PrivateKey:PublicKey));return key;}catch{key.Dispose();throw;}}
    public object State(string province){var s=catalogs.Revisions.Read(province)??throw new ArgumentException();return new{province,masterVersion=s.Base.Version,localVersion=s.Effective.Version,legacy=s.Legacy,conflicts=s.Conflicts,canPrepare=CanPrepare,hasTrust=HasTrust,history=catalogs.Revisions.History(province)};}
    public SignedCatalog Prepare(string province,string version,AuditActor actor)
    {
        if(!CanPrepare)throw new AccessDeniedException("Esta instalación no es la herramienta maestra de Viteka.");
        var s=catalogs.Revisions.Read(province)??throw new ArgumentException();
        if(s.Legacy||s.Conflicts.Length>0)throw new ArgumentException("Resuelve las personalizaciones heredadas antes de preparar una publicación maestra.");
        var delta=CatalogRevisions.Build(s.Base,s.Effective,version);using var key=ReadKey(true);
        catalogs.Database.Record(actor,"catalog.prepare","ok",province+" · "+version+" · "+delta.Changes.Length+" altas, cambios o bajas de grupos",utility:UtilityCatalog.TenenciaId);return CatalogRevisions.Sign(delta,key);
    }
    public CatalogDelta Inspect(SignedCatalog package){
        if(!HasTrust)throw new ArgumentException("Falta aprovisionar la clave pública de Viteka en el servidor.");
        try{using var key=ReadKey();return CatalogRevisions.Verify(package,key,new Version(0,18,0));}
        catch(Exception e)when(e is InvalidDataException or FormatException or JsonException or CryptographicException){throw new ArgumentException("No se pudo verificar el paquete: firma, formato o versión incompatible. No se ha aplicado.",e);}
    }
    public object Preview(string province,SignedCatalog package)
    {
        var d=Inspect(package);if(d.Province!=province)throw new ArgumentException("El paquete no corresponde a la provincia de la farmacia.");var state=catalogs.Revisions.Read(province)??throw new ArgumentException();
        return new{id=d.Id,fromVersion=d.FromVersion,toVersion=d.ToVersion,alreadyApplied=state.Applied.Contains(d.Id),missingPrevious=state.Base.Version!=d.FromVersion,changes=d.Changes.Select(x=>new{x.Action,x.GroupId,name=x.Group?.Name,codes=x.Group?.Codes.Length})};
    }
    public object Apply(string province,SignedCatalog package,string expected,AuditActor actor){var delta=Inspect(package);if(delta.Province!=province)throw new ArgumentException("Provincia incompatible.");return catalogs.Revisions.Apply(delta,expected,actor);}
    public async Task<object> Check(string province,CancellationToken ct)
    {
        using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(20)};http.DefaultRequestHeaders.UserAgent.ParseAdd("Viteka-Catalogs/0.18");
        using var response=await http.GetAsync("https://api.github.com/repos/RafaViteka/Viteka_Utilidades/releases?per_page=30",HttpCompletionOption.ResponseHeadersRead,ct);response.EnsureSuccessStatusCode();
        using var input=await response.Content.ReadAsStreamAsync(ct);using var bytes=new MemoryStream();var block=new byte[65536];int count;
        while((count=await input.ReadAsync(block,ct))>0){if(bytes.Length+count>2*1024*1024)throw new InvalidDataException("Respuesta de GitHub demasiado grande.");bytes.Write(block,0,count);}
        using var json=JsonDocument.Parse(bytes.ToArray());var updates=new List<object>();
        foreach(var release in json.RootElement.EnumerateArray()){
            if(release.GetProperty("draft").GetBoolean())continue;
            foreach(var asset in release.GetProperty("assets").EnumerateArray()){
                var name=asset.GetProperty("name").GetString()??"";var url=asset.GetProperty("browser_download_url").GetString()??"";
                if(!name.StartsWith("tenencia-"+province+"-",StringComparison.Ordinal)||!name.EndsWith(".vcat",StringComparison.Ordinal)||!Uri.TryCreate(url,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.Host!="github.com"||!uri.AbsolutePath.StartsWith("/RafaViteka/Viteka_Utilidades/releases/download/",StringComparison.Ordinal))continue;
                updates.Add(new{name,url,size=asset.GetProperty("size").GetInt64()});
            }
        }
        return new{updates,message=updates.Count==0?"No hay paquetes de datos publicados para esta provincia.":"Descarga el paquete, revisa su firma e importa las versiones pendientes por orden."};
    }
}
