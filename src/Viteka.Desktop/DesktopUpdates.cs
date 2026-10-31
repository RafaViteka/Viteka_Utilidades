using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Viteka.Core;

namespace Viteka.Desktop;

internal sealed class DesktopUpdates : IDisposable
{
    private readonly UpdateJournal journal;
    private readonly Func<Task<AccessUserView>> currentUser;
    private readonly Action<object> post;
    private readonly Func<bool> isLocalServer;
    private readonly Action saveSession;
    private readonly Action discardSession;
    private readonly Func<string,string?,Task> audit;
    private readonly Func<Task<string>> reviewIdentity;
    private async Task<PreviewUpdater> Checker(HttpClient http)=>new(http,ProductProfile.IsReview?await reviewIdentity():null);
    private readonly HttpClient readHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpClient downloadHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    private readonly string directory;
    private readonly Version version = typeof(MainWindow).Assembly.GetName().Version!;
    internal bool IsBusy { get; private set; }
    private CancellationTokenSource? downloadCancellation;
    private bool cancellable;
    private void Phase(string phase,int? percent=null)=>post(new{type="update-phase",phase,percent});
    private sealed class DownloadProgress(Action<int> emit):IProgress<int>{public void Report(int value)=>emit(value);}
    internal DesktopUpdates(string directory, Func<Task<AccessUserView>> currentUser, Action<object> post, Func<bool> isLocalServer,
        Action saveSession, Action discardSession, Func<string,string?,Task> audit, Func<Task<string>> reviewIdentity)
    {
        this.directory = directory; this.currentUser = currentUser; this.post = post; this.isLocalServer = isLocalServer;
        this.saveSession = saveSession; this.discardSession = discardSession; this.audit=audit;this.reviewIdentity=reviewIdentity;
        journal = new UpdateJournal(directory);
        journal.RecordRunningVersion(version);
    }
    internal object State(string? message = null)
    {
        var state = journal.Load();
        var available = Version.TryParse(state.AvailableVersion, out var offered) && offered > new Version(version.Major, version.Minor, version.Build);
        return new
        {
            available,
            version = available ? state.AvailableVersion : null,
            lastChecked = state.LastAttempt,
            lastSuccess = state.LastSuccess,
            canInstallHere = isLocalServer(),
            checking = IsBusy,
            message
        };
    }
    internal async Task<object> CheckAsync(bool manual, CancellationToken ct)
    {
        if (IsBusy || !journal.TryBeginCheck(manual)) return State();
        IsBusy = true; post(new { type = "update-state", state = State() });
        try
        {
            var offer = await (await Checker(readHttp)).FindAsync(version, ct);
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
                try { published = await (await Checker(readHttp)).GetHistoryAsync(ct); }
                catch (Exception failure) when (failure is HttpRequestException or IOException or JsonException or OperationCanceledException) { error = "No se pudieron cargar las publicaciones de GitHub. El historial de este equipo sigue disponible."; }
                return new { local, published = published.Select(e => new { version = e.Version, title = e.Title, notes = e.Notes, publishedAt = e.PublishedAt }), message = error };
            case "update.publication":
                var offered = await (await Checker(readHttp)).FindAsync(version, ct);
                if (offered is null) return new { opened = false };
                using (Process.Start(new ProcessStartInfo(offered.ReleasePage.AbsoluteUri) { UseShellExecute = true })) { }
                return new { opened = true };
            case "update.cancel":
                if(cancellable)downloadCancellation?.Cancel();return new{cancelled=cancellable};
            case "update.install":
            {
                if (!AccessPolicy.CanInstallUpdates(actor)) throw new AccessDeniedException();
                if (!isLocalServer()) throw new AccessDeniedException("Ejecuta esta actualización desde el servidor de la farmacia. Los puestos utilizan su instalación compartida.");
                if (IsBusy) return new { cancelled = true };
                using var downloadGate=new Semaphore(1,1,"Global\\"+ProductProfile.ShellAppId+"Download");
                using var installGate=new Semaphore(1,1,"Global\\"+ProductProfile.ShellAppId+"Install");
                if(!downloadGate.WaitOne(0))throw new InvalidOperationException("Ya hay una descarga de actualización en curso.");
                if(!installGate.WaitOne(0)){downloadGate.Release();throw new InvalidOperationException("Ya hay una instalación en curso.");}installGate.Release();
                IsBusy = true;
                downloadCancellation=CancellationTokenSource.CreateLinkedTokenSource(ct);ct=downloadCancellation.Token;cancellable=true;
                Phase("preparing");
                try
                {
                    // Fetch a fresh, verified offer; do not trust a persisted filename/digest.
                    var update = await (await Checker(readHttp)).FindAsync(version, ct) ?? throw new InvalidOperationException("No hay una versión posterior verificable.");
                    var progress = new DownloadProgress(percent => Phase("downloading",percent));
                    using var installer = await (await Checker(downloadHttp)).DownloadAsync(update, Path.Combine(directory, "Updates"), progress, ct, ()=>Phase("verifying"));
                    if(ProductProfile.IsReview && FileVersionInfo.GetVersionInfo(installer.Path).ProductName!="Viteka Utilidades - Pruebas")throw new InvalidDataException("El paquete no corresponde a la edición de pruebas. No se instalará.");
                    if (!AccessPolicy.CanInstallUpdates(await currentUser())) throw new AccessDeniedException();
                    // Instalación desatendida: sin asistente ni preguntas. Windows solo pide su confirmación de permisos (UAC).
                    // El instalador vuelve a abrir Viteka al terminar (/RELAUNCH=1) y la sesión se recupera sin pedir contraseña.
                    saveSession();
                    var log = Path.Combine(Path.GetDirectoryName(installer.Path)!, "instalacion.log");
                    cancellable=false;Phase("handoff");
                    try { await audit("update.requested",update.Version.ToString(3));await UpdateCompanion.PrepareAsync(installer.Path,update.Sha256,update.Size,log,update.Version.ToString(3),ct); }
                    catch { discardSession(); throw; }
                    try{await audit("update.started",update.Version.ToString(3));}catch(Exception e)when(e is HttpRequestException or OperationCanceledException or AccessSessionException or AccessDeniedException){/* The service may already be restarting. Requested was persisted before handoff. */}
                    post(new { type = "update-installing" });
                    try { journal.RecordInstallerStarted(update.Version); } catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
                    Application.Current.Shutdown(); return new { started = true };
                }
                catch(OperationCanceledException){Phase("cancelled");return new{cancelled=true};}
                catch {Phase("failed");try{await audit("update.failed",null);}catch(Exception e)when(e is HttpRequestException or OperationCanceledException or AccessSessionException or AccessDeniedException){}throw;}
                finally { IsBusy = false;cancellable=false;downloadCancellation.Dispose();downloadCancellation=null;downloadGate.Release(); }
            }
            default: throw new InvalidOperationException();
        }
    }
    /// <summary>Parámetros de Inno Setup para una actualización silenciosa que reabre la aplicación al terminar.</summary>
    internal static string SilentArguments(string log) =>
        $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /CLOSEAPPLICATIONS /RELAUNCH=1 \"/LOG={log}\"";
    public void Dispose() { readHttp.Dispose(); downloadHttp.Dispose(); }
}
