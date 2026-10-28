using System.IO;
using System.Text.Json;
using System.Windows;

namespace Viteka.Desktop;

internal sealed class NixFarmaConnectionController(Window owner, string directory, AccessServiceClient access)
{
    private readonly NixFarmaConnectionStore legacy = new(directory);
    private bool busy;
    internal bool IsBusy => busy;
    private async Task<object> Status()
    {
        var state = await access.OracleAsync("state");
        // Preserve the verified legacy connection when upgrading on its original server.
        // It moves only over pinned TLS, after superadministrator authorization.
        if (access.IsLocalServer && legacy.Exists)
        {
            if (!state.GetProperty("configured").GetBoolean())
            {
                var old = legacy.Load();
                if (old is not null) await access.OracleAsync("configure", new { dsn = old.Dsn, user = old.User, password = old.Password, keepPassword = false });
            }
            legacy.Forget(); state = await access.OracleAsync("state");
        }
        return state;
    }
    internal async Task<object> HandleAsync(string command, CancellationToken ct)
    {
        if (IsBusy) return new { busy = true, message = "Hay una operación de conexión en curso." };
        busy = true;
        try
        {
            if (command == "nixfarma.status") return await Status();
            if (command == "nixfarma.test") return await access.OracleAsync("test");
            if (command == "nixfarma.forget")
            {
                if (MessageBox.Show(owner, "Se eliminará la conexión Oracle guardada en el servidor para esta farmacia. No se modificará el DSN ni la base de datos. ¿Continuar?", "Olvidar conexión", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return new { cancelled = true, state = await Status() };
                return await access.OracleAsync("forget");
            }
            if (command == "nixfarma.configure")
            {
                var state = (JsonElement)await Status();
                var saved = state.GetProperty("configured").GetBoolean();
                var prior = new NixFarmaSettings { Dsn = state.GetProperty("dsn").GetString() ?? "NIXFARMA", User = state.GetProperty("user").GetString() ?? "" };
                var dialog = new NixFarmaConfigurationWindow(saved ? prior : null, saved) { Owner = owner, MaxHeight = Math.Max(240, Math.Min(660, owner.ActualHeight - 24)) };
                if (dialog.ShowDialog() != true || dialog.Settings is null) return new { cancelled = true, state };
                var settings = dialog.Settings;
                return await access.OracleAsync("configure", new { dsn = settings.Dsn, user = settings.User, password = settings.Password,
                    keepPassword = saved && settings.Password.Length == 0 && settings.Dsn == prior.Dsn && settings.User == prior.User });
            }
            throw new InvalidOperationException();
        }
        finally { busy = false; }
    }
}
