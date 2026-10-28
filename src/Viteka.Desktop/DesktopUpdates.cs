using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Viteka.Core;

namespace Viteka.Desktop;

internal sealed class DesktopUpdates : IDisposable
{
    private readonly Window owner;
    private readonly UpdateJournal journal;
    private readonly Func<Task<AccessUserView>> currentUser;
    private readonly Action<object> post;
    private readonly Func<bool> isLocalServer;
    private readonly HttpClient readHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient downloadHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string directory;
    private readonly Version version = typeof(MainWindow).Assembly.GetName().Version!;
    internal bool IsBusy { get; private set; }
    internal DesktopUpdates(Window owner, string directory, Func<Task<AccessUserView>> currentUser, Action<object> post, Func<bool> isLocalServer)
    {
        this.owner = owner; this.directory = directory; this.currentUser = currentUser; this.post = post; this.isLocalServer = isLocalServer;
        journal = new UpdateJournal(directory);
        journal.RecordRunningVersion(version);
    }
    internal object State(string? message = null)
    {
        var state = journal.Load();
        var available = Version.TryParse(state.AvailableVersion, out var offered) && offered > new Version(version.Major, version.Minor, version.Build);
        return new { available, version = available ? state.AvailableVersion : null, lastChecked = state.LastAttempt,
            lastSuccess = state.LastSuccess, canInstallHere = isLocalServer(), checking = IsBusy, message };
    }
    internal async Task<object> CheckAsync(bool manual, CancellationToken ct)
    {
        if (IsBusy || !journal.TryBeginCheck(manual)) return State();
        IsBusy = true; post(new { type = "update-state", state = State() });
        try
        {
            var offer = await new PreviewUpdater(readHttp).FindAsync(version, ct);
            journal.CompleteCheck(offer?.Version, true);
            IsBusy = false; var result = State(offer is null ? "No hay una versión posterior verificable disponible." : $"Versión {offer.Version.ToString(3)} disponible."); post(new { type = "update-state", state = result }); return result;
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
        {
            journal.CompleteCheck(null, false);
            IsBusy = false; var result = State("No se pudo comprobar GitHub. Puedes intentarlo manualmente; la próxima búsqueda automática se realizará en 24 horas.");
            post(new { type = "update-state", state = result }); return result;
        }
        finally { IsBusy = false; }
    }
    internal async Task<object> HandleAsync(string command, CancellationToken ct)
    {
        var actor = await currentUser();
        if (actor.MustChangePassword) throw new AccessDeniedException("Cambia la contraseña inicial antes de continuar.");
        switch (command)
        {
            case "update.state": return State();
            case "update.check": return await CheckAsync(true, ct);
            case "update.history":
                var local = journal.Load().Entries.Select(e => new { at = e.At, kind = e.Kind, version = e.Version }).ToArray();
                PublishedUpdate[] published = []; string? error = null;
                try { published = await new PreviewUpdater(readHttp).GetHistoryAsync(ct); }
                catch (Exception failure) when (failure is HttpRequestException or IOException or JsonException or OperationCanceledException) { error = "No se pudieron cargar las publicaciones de GitHub. El historial de este equipo sigue disponible."; }
                return new { local, published = published.Select(e => new { version = e.Version, title = e.Title, notes = e.Notes, publishedAt = e.PublishedAt }), message = error };
            case "update.publication":
                var offered = await new PreviewUpdater(readHttp).FindAsync(version, ct);
                if (offered is null) return new { opened = false };
                using (Process.Start(new ProcessStartInfo(offered.ReleasePage.AbsoluteUri) { UseShellExecute = true })) { }
                return new { opened = true };
            case "update.install":
                if (!AccessPolicy.CanInstallUpdates(actor)) throw new AccessDeniedException();
                if (!isLocalServer()) throw new AccessDeniedException("Ejecuta esta actualización desde el servidor de la farmacia. Los puestos utilizan su instalación compartida.");
                if (IsBusy) return new { cancelled = true };
                IsBusy = true;
                try
                {
                    // Fetch a fresh, verified offer; do not trust a persisted filename/digest.
                    var update = await new PreviewUpdater(readHttp).FindAsync(version, ct) ?? throw new InvalidOperationException("No hay una versión posterior verificable.");
                    if (MessageBox.Show(owner, "Se descargará y verificará el instalador desde GitHub. Es una versión de prueba sin firma digital. Viteka se cerrará al iniciar la instalación y Windows podrá pedir permisos. Tus datos se conservarán. Cierra Viteka en los puestos que usen la carpeta compartida; el servicio se reiniciará y deberán volver a entrar. ¿Continuar?", "Actualizar Viteka Utilidades", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                        return new { cancelled = true };
                    var progress = new Progress<int>(percent => post(new { type = "update-progress", percent }));
                    using var installer = await new PreviewUpdater(downloadHttp).DownloadAsync(update, Path.Combine(directory, "Updates"), progress, ct);
                    if (!AccessPolicy.CanInstallUpdates(await currentUser())) throw new AccessDeniedException();
                    var process = Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true, Verb = "runas" }) ?? throw new InvalidOperationException();
                    process.Dispose();
                    try { journal.RecordInstallerStarted(update.Version); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                    Application.Current.Shutdown(); return new { started = true };
                }
                finally { IsBusy = false; }
            default: throw new InvalidOperationException();
        }
    }
    public void Dispose() { readHttp.Dispose(); downloadHttp.Dispose(); }
}
