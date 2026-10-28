using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Viteka.Desktop;

internal sealed class NixFarmaConnectionController(Window owner, string directory)
{
    private readonly NixFarmaConnectionStore store = new(directory);
    private readonly NixFarmaConnectionTester tester = new();
    private bool busy;
    internal bool IsBusy => busy || tester.IsRunning;
    private object State(NixFarmaSettings? settings, string? message = null)
    {
        bool available;
        try { available = NixFarmaConnectionStore.SystemDsnExists(settings?.Dsn ?? "NIXFARMA"); }
        catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException or IOException) { available = false; }
        return new { configured = settings is not null, saved = store.Exists, dsn = settings?.Dsn ?? "NIXFARMA", user = settings?.User ?? "", dsnAvailable = available, message };
    }
    internal object Status()
    {
        try { return State(store.Load()); }
        catch (Exception error) when (IsStorageError(error))
        { return State(null, "No se puede leer la conexión guardada para este usuario de Windows. Usa «Olvidar conexión» antes de configurarla de nuevo."); }
    }
    private static bool IsStorageError(Exception error) => error is IOException or UnauthorizedAccessException or CryptographicException or JsonException;
    internal async Task<object> HandleAsync(string command, CancellationToken ct)
    {
        if (command == "nixfarma.status") return Status();
        if (IsBusy) return new { busy = true, message = "Hay una operación de conexión en curso. Espera a que termine o vuelve a abrir Viteka si el controlador no responde." };
        busy = true;
        try
        {
            switch (command)
            {
                case "nixfarma.configure":
                    var dialog = new NixFarmaConfigurationWindow(store.Load()) { Owner = owner, MaxHeight = Math.Max(240, Math.Min(660, owner.ActualHeight - 24)) };
                    if (dialog.ShowDialog() != true || dialog.Settings is null) return new { cancelled = true, state = Status() };
                    store.Save(dialog.Settings);
                    return new { state = Status() };
                case "nixfarma.test":
                    var settings = store.Load();
                    if (settings is null) return new { success = false, code = "not_configured", message = "Configura primero el DSN y la cuenta Oracle en este servidor." };
                    var result = await tester.TestAsync(settings, ct);
                    return new { success = result.Success, code = result.Code, message = result.Message, sqlState = result.SqlState, oracleCode = result.OracleCode, checkedAt = DateTimeOffset.Now };
                case "nixfarma.forget":
                    if (MessageBox.Show(owner, "Se eliminará la conexión cifrada de este usuario de Windows. No se modificará NixFarma ni su DSN. ¿Continuar?", "Olvidar conexión", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return new { cancelled = true, state = Status() };
                    store.Forget();
                    return new { state = Status() };
                default: throw new InvalidOperationException();
            }
        }
        catch (Exception error) when (IsStorageError(error))
        { return new { message = "No se pudo leer o guardar la conexión cifrada de este usuario. Revisa sus permisos y, si es necesario, usa «Olvidar conexión» para configurarla de nuevo.", state = Status() }; }
        finally { busy = false; }
    }
}
