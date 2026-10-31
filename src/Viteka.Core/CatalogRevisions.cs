using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Viteka.Contracts;
namespace Viteka.Core;

public sealed record CatalogConflict(string GroupId,string Field,string Note);
public sealed record CatalogRevision(TenenciaList Base,TenenciaList Effective,bool Legacy,CatalogConflict[] Conflicts,string[] Applied);
public sealed record CatalogChange(string Action,string GroupId,TenenciaGroup? Group);
public sealed record CatalogDelta(int Schema,string Id,string Province,string FromVersion,string ToVersion,string MinimumAppVersion,CatalogChange[] Changes);
public sealed record SignedCatalog(string KeyId,string Payload,string Signature);
public sealed class CatalogRevisions(PharmacyDatabase database)
{
    public static string Fingerprint(TenenciaList list)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(list,PharmacyDatabase.Json))).ToLowerInvariant();
    public CatalogRevision Ensure(TenenciaList packaged,TenenciaList? legacy=null)=>database.Transaction((c,tx)=>{
        var json=PharmacyDatabase.Get(c,tx,"catalog."+packaged.Province);
        if(json is not null)return JsonSerializer.Deserialize<CatalogRevision>(json,PharmacyDatabase.Json)!;
        var state=new CatalogRevision(packaged,legacy??packaged,legacy is not null,legacy is null?[]:[new("*","migration","Listado heredado: revisa las personalizaciones antes de distribuirlo.")],[]);
        PharmacyDatabase.Set(c,tx,"catalog."+packaged.Province,state);
        PharmacyDatabase.Record(c,tx,new("system","Servicio Viteka"),legacy is null?"catalog.initialize":"catalog.migrate","ok",packaged.Province+" · "+state.Effective.Version+(legacy is null?"":" · legado conservado para revisión"),utility:UtilityCatalog.TenenciaId);return state;
    });
    public CatalogRevision? Read(string province)=>database.Transaction((c,tx)=>{
        var json=PharmacyDatabase.Get(c,tx,"catalog."+province);return json is null?null:JsonSerializer.Deserialize<CatalogRevision>(json,PharmacyDatabase.Json);
    });
    private CatalogRevision Change(string province,string expected,AuditActor actor,string action,Func<CatalogRevision,CatalogRevision> change,string? operation=null)=>database.Transaction((c,tx)=>{
        var old=JsonSerializer.Deserialize<CatalogRevision>(PharmacyDatabase.Get(c,tx,"catalog."+province)??throw new InvalidDataException(),PharmacyDatabase.Json)!;
        // Idempotent packages are harmless even if their original expected version has moved on.
        if(operation is not null&&old.Applied.Contains(operation))return old;
        if(old.Effective.Version!=expected)throw new InvalidOperationException("El listado cambió en otro puesto. Recarga antes de guardar.");
        var next=change(old);TenenciaRules.Validate(next.Base);TenenciaRules.Validate(next.Effective);
        if(JsonSerializer.SerializeToUtf8Bytes(next.Base,PharmacyDatabase.Json).Length>4*1024*1024||JsonSerializer.SerializeToUtf8Bytes(next.Effective,PharmacyDatabase.Json).Length>4*1024*1024)throw new ArgumentException("El listado supera el tamaño permitido.");
        using(var cmd=PharmacyDatabase.Command(c,tx,"INSERT INTO catalog_history(province,at,value) VALUES($p,$at,$value)",("$p",province),("$at",DateTimeOffset.UtcNow.ToString("O")),("$value",JsonSerializer.Serialize(old,PharmacyDatabase.Json))))cmd.ExecuteNonQuery();
        PharmacyDatabase.Set(c,tx,"catalog."+province,next);
        var previousCodes=old.Effective.Groups.SelectMany(g=>g.Codes.Select(cn=>g.Id+":"+cn)).ToHashSet();
        var newCodes=next.Effective.Groups.SelectMany(g=>g.Codes.Select(cn=>g.Id+":"+cn)).ToHashSet();
        var changed=old.Effective.Groups.Concat(next.Effective.Groups).Select(g=>g.Id).Distinct().Where(id=>!Equal(old.Effective.Groups.FirstOrDefault(g=>g.Id==id),next.Effective.Groups.FirstOrDefault(g=>g.Id==id))).ToArray();
        var note=province+" · "+next.Effective.Version+" · grupos: "+string.Join(", ",changed.Take(5))+" · "+newCodes.Except(previousCodes).Count()+" referencias añadidas · "+previousCodes.Except(newCodes).Count()+" retiradas";
        PharmacyDatabase.Record(c,tx,actor,action,"ok",note[..Math.Min(note.Length,600)],operation,UtilityCatalog.TenenciaId);
        return next;
    });
    public TenenciaList SaveLocal(TenenciaList next,string expected,AuditActor actor,string action)=>Change(next.Province,expected,actor,action,s=>s with {Effective=next}).Effective;
    public TenenciaList Reset(string province,string expected,AuditActor actor)=>Change(province,expected,actor,"catalog.reset",s=>s with{Effective=s.Base,Legacy=false,Conflicts=[]}).Effective;
    public object[] History(string province)=>database.Transaction((c,tx)=>{
        using var cmd=PharmacyDatabase.Command(c,tx,"SELECT id,at,value FROM catalog_history WHERE province=$p ORDER BY id DESC LIMIT 50",("$p",province));using var r=cmd.ExecuteReader();var rows=new List<object>();while(r.Read()){var s=JsonSerializer.Deserialize<CatalogRevision>(r.GetString(2),PharmacyDatabase.Json)!;rows.Add(new{id=r.GetInt64(0),at=r.GetString(1),version=s.Effective.Version});}return rows.ToArray();
    });
    public TenenciaList Rollback(string province,long revision,string expected,AuditActor actor)
    {
        var saved=database.Transaction((c,tx)=>{using var cmd=PharmacyDatabase.Command(c,tx,"SELECT value FROM catalog_history WHERE province=$p AND id=$id",("$p",province),("$id",revision));return cmd.ExecuteScalar() as string;});
        var previous=JsonSerializer.Deserialize<CatalogRevision>(saved??throw new ArgumentException("Revisión no disponible."),PharmacyDatabase.Json)!;
        return Change(province,expected,actor,"catalog.rollback",current=>current with{Effective=previous.Effective with{Version=NewVersion()},Legacy=true,Conflicts=[new("*","rollback","Vista anterior recuperada como personalización; la base maestra y los paquetes recibidos se conservan.")]}).Effective;
    }
    public static string NewVersion()=>"Local-"+DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];
    public static CatalogDelta Build(TenenciaList from,TenenciaList to,string version)
    {
        TenenciaRules.Validate(from);TenenciaRules.Validate(to);if(from.Province!=to.Province||version.Length is <1 or >100||version==from.Version)throw new ArgumentException("Versión maestra no válida.");
        var changes=new List<CatalogChange>();
        foreach(var g in from.Groups)if(!to.Groups.Any(n=>n.Id==g.Id))changes.Add(new("delete",g.Id,null));
        foreach(var g in to.Groups)if(!from.Groups.Any(o=>o.Id==g.Id&&Equal(o,g)))changes.Add(new("upsert",g.Id,g));
        return new(1,Guid.NewGuid().ToString("N"),to.Province,from.Version,version,"0.18.0",changes.ToArray());
    }
    public static SignedCatalog Sign(CatalogDelta delta,RSA key)
    {
        var payload=JsonSerializer.SerializeToUtf8Bytes(delta,PharmacyDatabase.Json);var id=Convert.ToHexString(SHA256.HashData(key.ExportSubjectPublicKeyInfo())).ToLowerInvariant();
        return new(id,Convert.ToBase64String(payload),Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pss)));
    }
    public static CatalogDelta Verify(SignedCatalog package,RSA trustedKey,Version appVersion)
    {
        if(package.Payload.Length>5_600_000||package.Signature.Length>1500)throw new ArgumentException("Paquete demasiado grande.");
        var id=Convert.ToHexString(SHA256.HashData(trustedKey.ExportSubjectPublicKeyInfo())).ToLowerInvariant();if(id!=package.KeyId)throw new InvalidDataException("La firma no pertenece a la clave de confianza de Viteka.");
        var payload=Convert.FromBase64String(package.Payload);
        if(!trustedKey.VerifyData(payload,Convert.FromBase64String(package.Signature),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))throw new InvalidDataException("Firma del listado no válida.");
        var delta=JsonSerializer.Deserialize<CatalogDelta>(payload,PharmacyDatabase.Json)??throw new InvalidDataException();
        if(delta.Schema!=1||!Guid.TryParseExact(delta.Id,"N",out _)||!Version.TryParse(delta.MinimumAppVersion,out var min)||appVersion<min||delta.Changes.Length>500||delta.Changes.Select(x=>x.GroupId).Distinct().Count()!=delta.Changes.Length||delta.FromVersion.Length>100||delta.ToVersion.Length is <1 or >100)throw new InvalidDataException("Paquete incompatible con esta aplicación.");return delta;
    }
    public CatalogRevision Apply(CatalogDelta delta,string expected,AuditActor actor)=>Change(delta.Province,expected,actor,"catalog.update",s=>{
        if(s.Base.Version!=delta.FromVersion)throw new InvalidOperationException("Falta una versión anterior del listado: "+delta.FromVersion+".");
        var target=s.Base.Groups.ToDictionary(g=>g.Id);
        foreach(var change in delta.Changes){if(change.Action=="delete"){if(!target.Remove(change.GroupId))throw new InvalidDataException("Baja de grupo inexistente.");}else if(change.Action=="upsert"&&change.Group is {} group&&group.Id==change.GroupId)target[group.Id]=group;else throw new InvalidDataException("Operación de listado no válida.");}
        var master=s.Base with{Version=delta.ToVersion,Groups=target.Values.ToArray(),ReviewedOn=DateOnly.FromDateTime(DateTime.UtcNow)};TenenciaRules.Validate(master);
        var merged=Merge(s.Base,s.Effective,master,s.Legacy);return new(master,merged.List,s.Legacy,[..s.Conflicts,..merged.Conflicts],s.Applied.Append(delta.Id).ToArray());
    },delta.Id);
    public CatalogRevision Resolve(string province,string groupId,string field,bool useMaster,string expected,AuditActor actor)=>Change(province,expected,actor,"catalog.conflict",s=>{
        if(!s.Conflicts.Any(x=>x.GroupId==groupId&&x.Field==field))throw new ArgumentException("Conflicto no disponible.");
        var groups=s.Effective.Groups.ToList();var master=s.Base.Groups.FirstOrDefault(g=>g.Id==groupId);var local=groups.FirstOrDefault(g=>g.Id==groupId);
        if(useMaster){if(field=="group"||local is null){groups.RemoveAll(g=>g.Id==groupId);if(master is not null)groups.Add(master);}else if(master is not null){var node=JsonSerializer.SerializeToNode(local,PharmacyDatabase.Json)!.AsObject();var remote=JsonSerializer.SerializeToNode(master,PharmacyDatabase.Json)!.AsObject();node[field]=remote[field]?.DeepClone();groups[groups.IndexOf(local)]=node.Deserialize<TenenciaGroup>(PharmacyDatabase.Json)!;}}
        return s with{Effective=s.Effective with{Version=NewVersion(),Groups=groups.ToArray()},Conflicts=s.Conflicts.Where(x=>x.GroupId!=groupId||x.Field!=field).ToArray(),Legacy=s.Legacy&&s.Conflicts.Length>1};
    });
    private static bool Equal<T>(T a,T b)=>JsonNode.DeepEquals(JsonSerializer.SerializeToNode(a,PharmacyDatabase.Json),JsonSerializer.SerializeToNode(b,PharmacyDatabase.Json));
    public static (TenenciaList List,CatalogConflict[] Conflicts) Merge(TenenciaList previous,TenenciaList local,TenenciaList next,bool legacy=false)
    {
        var conflicts=new List<CatalogConflict>();var result=new List<TenenciaGroup>();
        foreach(var id in previous.Groups.Select(g=>g.Id).Concat(local.Groups.Select(g=>g.Id)).Concat(next.Groups.Select(g=>g.Id)).Distinct()){
            var old=previous.Groups.FirstOrDefault(g=>g.Id==id);var mine=local.Groups.FirstOrDefault(g=>g.Id==id);var remote=next.Groups.FirstOrDefault(g=>g.Id==id);
            if(Equal(old,remote)){if(mine is not null)result.Add(mine);continue;}
            if(!legacy&&Equal(mine,old)){if(remote is not null)result.Add(remote);continue;}
            if(Equal(mine,remote)){if(mine is not null)result.Add(mine);continue;}
            if(legacy||old is null||mine is null||remote is null){if(mine is not null)result.Add(mine);conflicts.Add(new(id,"group","Cambio maestro y personalización local: se conserva la versión local hasta resolver."));continue;}
            var o=JsonSerializer.SerializeToNode(old,PharmacyDatabase.Json)!.AsObject();var m=JsonSerializer.SerializeToNode(mine,PharmacyDatabase.Json)!.AsObject();var n=JsonSerializer.SerializeToNode(remote,PharmacyDatabase.Json)!.AsObject();
            foreach(var field in n.Select(p=>p.Key).Where(k=>k is not ("codes" or "descriptions"))){if(JsonNode.DeepEquals(m[field],o[field]))m[field]=n[field]?.DeepClone();else if(!JsonNode.DeepEquals(n[field],o[field])&&!JsonNode.DeepEquals(m[field],n[field]))conflicts.Add(new(id,field,"Campo modificado en Viteka y en la farmacia; se conserva el valor local."));}
            var codes=new List<string>();var descriptions=new Dictionary<string,string>();
            foreach(var code in old.Codes.Concat(mine.Codes).Concat(remote.Codes).Distinct()){
                bool was=old.Codes.Contains(code),here=mine.Codes.Contains(code),there=remote.Codes.Contains(code);
                bool keep=here==was?there:here;
                var a=old.Descriptions?.GetValueOrDefault(code);var b=mine.Descriptions?.GetValueOrDefault(code);var d=remote.Descriptions?.GetValueOrDefault(code);
                if(was&&!there&&here&&b!=a){keep=true;conflicts.Add(new(id,"group","Artículo retirado del maestro con descripción local modificada: "+code));}
                if(was&&!here&&there&&d!=a)conflicts.Add(new(id,"group","Artículo retirado localmente y modificado en el maestro: "+code));
                if(!keep)continue;codes.Add(code);var text=b==a?d:b;if(b!=a&&d!=a&&b!=d)conflicts.Add(new(id,"group","Descripción modificada en ambos orígenes: "+code));if(!string.IsNullOrEmpty(text))descriptions[code]=text;
            }
            m["codes"]=JsonSerializer.SerializeToNode(codes);m["descriptions"]=JsonSerializer.SerializeToNode(descriptions);result.Add(m.Deserialize<TenenciaGroup>(PharmacyDatabase.Json)!);
        }
        var list=Equal(result.ToArray(),next.Groups)?next:next with{Version=NewVersion(),Groups=result.ToArray()};TenenciaRules.Validate(list);return(list,conflicts.Distinct().ToArray());
    }
}
