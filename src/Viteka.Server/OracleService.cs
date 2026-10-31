using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Viteka.Server;

internal sealed class OracleProfile
{
    public OracleProfile() { }
    public int Schema { get; set; } = 1;
    public string Dsn { get; set; } = "NIXFARMA";
    public string User { get; set; } = "";
    public string Password { get; set; } = "";
    public void Validate()
    {
        if (Schema != 1 || string.IsNullOrWhiteSpace(Dsn) || Dsn.Length > 32 || Dsn.Any(char.IsControl) ||
            string.IsNullOrWhiteSpace(User) || User.Length > 128 || User.Any(char.IsControl) || string.IsNullOrEmpty(Password) || Password.Length > 1024) throw new InvalidDataException();
    }
}
internal sealed class OracleService(string directory)
{
    private readonly NixFarmaConnectionTester tester = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private string FilePath => Path.Combine(directory, "oracle.dat");
    internal async Task<object> HandleAsync(string action, JsonElement data, CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return new { busy = true, message = "Hay una operación de conexión en curso." };
        try
        {
            var settings = Read();
            switch (action)
            {
                case "state": return State(settings);
                case "configure":
                    var next = new OracleProfile { Dsn = data.GetProperty("dsn").GetString() ?? "", User = data.GetProperty("user").GetString() ?? "", Password = data.GetProperty("password").GetString() ?? "" };
                    if (data.TryGetProperty("keepPassword", out var keep) && keep.GetBoolean())
                    {
                        if (settings is null || settings.Dsn != next.Dsn || settings.User != next.User) throw new ArgumentException("La conexión ha cambiado. Vuelve a introducir la contraseña Oracle.");
                        next.Password = settings.Password;
                    }
                    next.Validate(); Save(next); return new { state = State(next) };
                case "forget":
                    File.Delete(FilePath); return new { state = State(null) };
                case "test":
                    if (settings is null) return new { success = false, code = "not_configured", message = "Configura primero la conexión Oracle del servidor." };
                    var result = await tester.TestAsync(settings, ct);
                    return new { success = result.Success, code = result.Code, message = result.Message, sqlState = result.SqlState, oracleCode = result.OracleCode, checkedAt = DateTimeOffset.Now };
                default: throw new ArgumentException();
            }
        }
        finally { gate.Release(); }
    }
    internal async Task<Viteka.Contracts.TenenciaResponse> TenenciaAsync(string province, Viteka.Contracts.TenenciaList? list, bool inspectOnly, CancellationToken ct)
    {
        if (tester.IsRunning || !await gate.WaitAsync(0, ct)) return new("busy", "Hay una operación Oracle en curso.", province);
        var operation = Task.Run(() =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(28));
            try
            {
                if (!OperatingSystem.IsWindows()) return new Viteka.Contracts.TenenciaResponse("driver", "Se requiere Windows.", province);
                var profile = Read(); if (profile is null) return new("not_configured", "Configura primero la conexión a Nixfarma.", province);
                var builder = new System.Data.Odbc.OdbcConnectionStringBuilder { Dsn = profile.Dsn }; builder["UID"] = profile.User; builder["PWD"] = profile.Password;
                using var connection = new System.Data.Odbc.OdbcConnection(builder.ConnectionString);
                return Viteka.Core.TenenciaStockQuery.ReadOnly(connection, tx =>
                {
                    deadline.Token.ThrowIfCancellationRequested(); var schema = Viteka.Core.TenenciaStockQuery.Inspect(connection, tx, nativeNixFarma: true);
                    if (inspectOnly) return new Viteka.Contracts.TenenciaResponse("schema_verified", "Estructura verificada: APPUL.AB_ARTICULOS y APPUL.AB_ARTICULOS_FICHA_E; stock " + schema.Stock + "; lectura por CN explícitos. Caducidad: " + (schema.Expiry ?? "no identificada") + "; última compra: " + (schema.LastPurchase ?? "no identificada") + "; última venta: " + (schema.LastSale ?? "no identificada") + "; agrupación homogénea: " + (schema.Homogeneous.Length > 0 ? string.Join(", ", schema.Homogeneous) : "no identificada") + ". Columnas de AB_ARTICULOS: " + string.Join(", ", schema.ArticleColumns.Take(250)) + ". Columnas de AB_ARTICULOS_FICHA_E: " + string.Join(", ", schema.StockColumns.Take(250)) + ". No se han leído existencias.", province) { Fields = schema.Fields };
                    if (list is null) throw new InvalidOperationException();
                    var resolution = Viteka.Core.TenenciaArticleResolver.Resolve(connection, tx, schema, list, deadline.Token);
                    var codes = resolution.Groups.Values.SelectMany(c => c).Distinct().ToArray();
                    var stocks = codes.Length == 0 ? new Dictionary<string, Viteka.Contracts.StockArticle>() : Viteka.Core.TenenciaStockQuery.Read(connection, tx, schema, codes, deadline.Token);
                    return new Viteka.Contracts.TenenciaResponse("ok", "Consulta de solo lectura completada.", province, list.Version, list.Source, DateTimeOffset.UtcNow, Viteka.Core.TenenciaRules.Evaluate(list, stocks, resolution.Groups, resolution.Notes)) { Fields = schema.Fields };
                });
            }
            catch (OperationCanceledException) { return new("timeout", "La lectura se ha cancelado o ha agotado el tiempo disponible.", province); }
            catch (System.Data.Odbc.OdbcException error)
            {
                if (!OperatingSystem.IsWindows()) throw; var result = NixFarmaConnectionTester.Classify(error);
                // Classify exposes only fixed messages and validated structured codes, never raw ODBC text.
                var diagnostic = "Diagnóstico: " + result.Code + (result.SqlState is null ? "" : " · SQLSTATE " + result.SqlState) + (result.OracleCode is null ? "" : " · ORA-" + result.OracleCode.Value.ToString("D5", System.Globalization.CultureInfo.InvariantCulture));
                return new Viteka.Contracts.TenenciaResponse(result.Code, (inspectOnly ? "Comprobación de tablas: " : "Consulta de existencias: ") + result.Message + " " + diagnostic, province);
            }
            catch (InvalidDataException error) { return new("schema_or_data", error.Message.Length > 0 ? error.Message : "No se reconoce la estructura o los datos. Viteka debe revisarlos.", province); }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or DllNotFoundException or BadImageFormatException or NotSupportedException or FormatException or OverflowException or InvalidCastException) { return new("driver_or_data", "No se pudo completar la lectura. Revisa el controlador, las tablas y los datos con Viteka.", province); }
            finally { gate.Release(); }
        });
        try { return await operation.WaitAsync(TimeSpan.FromSeconds(30), ct); } catch (TimeoutException) { return new("timeout", "El controlador no respondió en 30 segundos. Espera a que termine antes de repetir.", province); }
    }
    /// <summary>Búsqueda de artículos de solo lectura para el editor del listado (solo Viteka).</summary>
    internal async Task<Viteka.Contracts.ArticleSearchResult> SearchArticlesAsync(string mode, string text, string? field, CancellationToken ct)
    {
        if (tester.IsRunning || !await gate.WaitAsync(0, ct)) return new("busy", "Hay una operación Oracle en curso.", []);
        var operation = Task.Run(() =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(28));
            try
            {
                if (!OperatingSystem.IsWindows()) return new Viteka.Contracts.ArticleSearchResult("driver", "Se requiere Windows.", []);
                var profile = Read(); if (profile is null) return new("not_configured", "Configura primero la conexión a Nixfarma.", []);
                var builder = new System.Data.Odbc.OdbcConnectionStringBuilder { Dsn = profile.Dsn }; builder["UID"] = profile.User; builder["PWD"] = profile.Password;
                using var connection = new System.Data.Odbc.OdbcConnection(builder.ConnectionString);
                return Viteka.Core.TenenciaStockQuery.ReadOnly(connection, tx => Viteka.Core.TenenciaArticleFinder.Search(connection, tx, Viteka.Core.TenenciaStockQuery.Inspect(connection, tx, nativeNixFarma: true), mode, text, field, deadline.Token));
            }
            catch (OperationCanceledException) { return new("timeout", "La búsqueda se ha cancelado o ha agotado el tiempo disponible.", []); }
            catch (System.Data.Odbc.OdbcException error)
            {
                if (!OperatingSystem.IsWindows()) throw; var result = NixFarmaConnectionTester.Classify(error);
                return new Viteka.Contracts.ArticleSearchResult(result.Code, "Búsqueda de artículos: " + result.Message, []);
            }
            catch (InvalidDataException error) { return new("schema_or_data", error.Message.Length > 0 ? error.Message : "No se reconoce la estructura o los datos.", []); }
            finally { gate.Release(); }
        });
        try { return await operation.WaitAsync(TimeSpan.FromSeconds(30), ct); } catch (TimeoutException) { return new("timeout", "El controlador no respondió en 30 segundos.", []); }
    }
    private object State(OracleProfile? profile)
    {
        bool available = false;
        if (OperatingSystem.IsWindows())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var sources = machine.OpenSubKey(@"SOFTWARE\ODBC\ODBC.INI\ODBC Data Sources");
            available = sources?.GetValue(profile?.Dsn ?? "NIXFARMA") is string;
        }
        return new { configured = profile is not null, saved = File.Exists(FilePath), dsn = profile?.Dsn ?? "NIXFARMA", user = profile?.User ?? "", dsnAvailable = available };
    }
    private OracleProfile? Read()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (!File.Exists(FilePath)) return null;
        if (new FileInfo(FilePath).Length > 65536) throw new InvalidDataException();
        var clear = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.LocalMachine);
        try { var result = JsonSerializer.Deserialize<OracleProfile>(clear) ?? throw new InvalidDataException(); result.Validate(); return result; }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }
    private void Save(OracleProfile settings)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings); byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.LocalMachine); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, encrypted); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
