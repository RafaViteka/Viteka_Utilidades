using System.Text.Json;
using System.Text.RegularExpressions;
namespace Viteka.Core;

/// <summary>Non-secret review ID. The persisted identity is bound to this Windows machine through DPAPI.</summary>
public sealed class ReviewIdentity(string directory,Func<byte[],byte[]> protect,Func<byte[],byte[]> unprotect)
{
    private sealed record Identity(int Schema,string Id);
    public static bool Valid(string? value)=>value is not null&&Regex.IsMatch(value,"^[a-f0-9]{32}$",RegexOptions.NonBacktracking);
    public string LoadOrCreate()
    {
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"review-device.dat");
        if(File.Exists(path)){
            if(new FileInfo(path).Length>8192)throw new InvalidDataException("Identidad de pruebas no válida.");
            var saved=JsonSerializer.Deserialize<Identity>(unprotect(File.ReadAllBytes(path)))??throw new InvalidDataException();
            if(saved.Schema!=1||!Valid(saved.Id))throw new InvalidDataException("Identidad de pruebas no válida.");return saved.Id;
        }
        var id=Guid.NewGuid().ToString("N");var temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(protect(JsonSerializer.SerializeToUtf8Bytes(new Identity(1,id))));file.Flush(true);}File.Move(temporary,path,false);}
        catch(IOException)when(File.Exists(path)){return LoadOrCreate();}
        finally{if(File.Exists(temporary))File.Delete(temporary);}
        return id;
    }
}
