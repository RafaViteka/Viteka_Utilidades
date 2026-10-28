using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Viteka.Contracts;
namespace Viteka.Core;

public static class SpanishProvinces
{
    public static IReadOnlyDictionary<string,string> Items { get; } = new Dictionary<string,string> {
        ["01"]="Araba/Álava",["02"]="Albacete",["03"]="Alicante",["04"]="Almería",["05"]="Ávila",["06"]="Badajoz",["07"]="Illes Balears",["08"]="Barcelona",["09"]="Burgos",["10"]="Cáceres",["11"]="Cádiz",["12"]="Castellón",["13"]="Ciudad Real",["14"]="Córdoba",["15"]="A Coruña",["16"]="Cuenca",["17"]="Girona",["18"]="Granada",["19"]="Guadalajara",["20"]="Gipuzkoa",["21"]="Huelva",["22"]="Huesca",["23"]="Jaén",["24"]="León",["25"]="Lleida",["26"]="La Rioja",["27"]="Lugo",["28"]="Madrid",["29"]="Málaga",["30"]="Murcia",["31"]="Navarra",["32"]="Ourense",["33"]="Asturias",["34"]="Palencia",["35"]="Las Palmas",["36"]="Pontevedra",["37"]="Salamanca",["38"]="Santa Cruz de Tenerife",["39"]="Cantabria",["40"]="Segovia",["41"]="Sevilla",["42"]="Soria",["43"]="Tarragona",["44"]="Teruel",["45"]="Toledo",["46"]="Valencia",["47"]="Valladolid",["48"]="Bizkaia",["49"]="Zamora",["50"]="Zaragoza",["51"]="Ceuta",["52"]="Melilla" };
}
public sealed class PharmacyDirectory(string directory, Func<byte[],byte[]> protect, Func<byte[],byte[]> unprotect)
{
    private readonly object gate = new();
    private string FilePath => Path.Combine(directory,"pharmacy.dat");
    public PharmacyProfile? Load() { lock(gate) { if(!File.Exists(FilePath))return null; if(new FileInfo(FilePath).Length>65536)throw new InvalidDataException();var p=JsonSerializer.Deserialize<PharmacyProfile>(unprotect(File.ReadAllBytes(FilePath)))??throw new InvalidDataException();Validate(p);return p; } }
    public void Save(PharmacyProfile profile)
    {
        Validate(profile);lock(gate){Directory.CreateDirectory(directory);var temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,protect(JsonSerializer.SerializeToUtf8Bytes(profile)));File.Move(temp,FilePath,true);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    public static void Validate(PharmacyProfile p)
    {
        if(p is null || !SpanishProvinces.Items.ContainsKey(p.Province??""))throw new ArgumentException("Selecciona la provincia de la farmacia.");
        if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Town))throw new ArgumentException("Indica el nombre y la localidad de la farmacia.");
        var fields=new[]{p.Name,p.Town,p.Email,p.Phone,p.Address,p.Postcode};
        if(fields.Any(x=>x is null||x.Length>200||x.Any(char.IsControl)))throw new ArgumentException("Revisa los datos de la farmacia (máximo 200 caracteres por campo).");
        if(p.Email.Length>0&&(!MailAddress.TryCreate(p.Email,out var mail)||mail.Address!=p.Email))throw new ArgumentException("El correo no es válido.");
        if(p.Postcode.Length>0&&(!Regex.IsMatch(p.Postcode,"^[0-9]{5}$")||!p.Postcode.StartsWith(p.Province!,StringComparison.Ordinal)))throw new ArgumentException("El código postal debe corresponder a la provincia seleccionada.");
    }
}
