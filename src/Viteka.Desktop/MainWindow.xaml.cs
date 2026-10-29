using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Viteka.Core;
namespace Viteka.Desktop;

public partial class MainWindow : Window
{
    private const string Origin = "https://app.viteka.local";
    private readonly LocalStateStore store = App.SmokeTest ? new LocalStateStore(Path.Combine(Path.GetTempPath(), "VitekaUiSmoke-" + Guid.NewGuid().ToString("N"))) : LocalStateStore.ForCurrentUser();
    private readonly WindowsShellIntegration shell;
    private NixFarmaConnectionController? nixfarma;
    private bool smokeStarted;
    private readonly CancellationTokenSource lifetime = new();
    private AccessServiceClient? access;
    private DesktopUpdates? updates;
    private System.Windows.Threading.DispatcherTimer? maintenance;
    private LocalStateStore? profile;
    private string? profileId;
    private AccessServiceClient Access => access ?? throw new InvalidOperationException();
    private LocalStateStore Preferences => profile ?? store;
    private bool closing;
    public MainWindow()
    {
        InitializeComponent();
        shell = new WindowsShellIntegration(this);
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        // Install the monitor work-area hook before maximizing; keep restore/minimize controls available.
        SourceInitialized += (_, _) => WindowState = WindowState.Maximized;
        Loaded += async (_, _) => await InitializeBrowser();
        Closed += (_, _) => { closing = true; lifetime.Cancel(); maintenance?.Stop(); Browser.Dispose(); shell.Dispose(); updates?.Dispose(); access?.Dispose(); lifetime.Dispose(); };
    }
    private async Task InitializeBrowser()
    {
        try
        {
            access ??= new AccessServiceClient();
            nixfarma ??= new NixFarmaConnectionController(this, store.DirectoryPath, Access);
            updates ??= new DesktopUpdates(this, store.DirectoryPath, () => Access.RequireUserAsync(), Post, () => Access.IsLocalServer);
            var files = Path.Combine(AppContext.BaseDirectory, "Web");
            if (!File.Exists(Path.Combine(files, "index.html"))) throw new FileNotFoundException();
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.DirectoryPath, "WebView2"));
            if (closing) return;
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.viteka.local", files, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false; // Angular handles Ctrl+wheel consistently; native toolbar remains outside page zoom.
            Browser.ZoomFactorChanged += (_, _) => {
                var percent = (int)Math.Round(Browser.ZoomFactor * 100);
                ZoomLabel.Content = percent + " %";
                if (!closing) core.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "zoom-changed", percent }));
            };
            try { SetZoom(store.Load().ZoomPercent, false); } catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException) { SetZoom(100, false); }
            ZoomBar.IsEnabled = true;
            core.NavigationStarting += (_, e) => { if (!Trusted(e.Uri)) e.Cancel = true; };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += ReceiveMessage;
            core.NavigationCompleted += async (_, e) => {
                if (e.IsSuccess) {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    if (!App.SmokeTest && maintenance is null)
                    {
                        maintenance = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
                        maintenance.Tick += async (_, _) => await ScheduledUpdateCheck(); maintenance.Start();
                        _ = ScheduledUpdateCheck();
                    }
                    if (App.SmokeTest && !smokeStarted) { smokeStarted = true; await RunNativeSmoke(); }
                } else { LoadingMessage.Text = "No se pudo cargar la interfaz. Cierra y vuelve a abrir Viteka."; if (App.SmokeTest) FinishSmoke(false, "navigation"); }
            };
            core.ProcessFailed += (_, _) => { LoadingPanel.Visibility = Visibility.Visible; LoadingMessage.Text = "La interfaz se ha detenido. Cierra y vuelve a abrir Viteka."; };
            core.Navigate(Origin + "/index.html?v=" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
        }
        catch (WebView2RuntimeNotFoundException)
        { LoadingMessage.Text = "Necesitas el runtime Microsoft Edge WebView2 para abrir la interfaz. Instálalo y vuelve a abrir Viteka."; RuntimeHelp.Visibility = Visibility.Visible; if (App.SmokeTest) FinishSmoke(false, "runtime"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException or System.Runtime.InteropServices.COMException)
        { LoadingMessage.Text = "No se pudo preparar la interfaz. Reinstala la versión completa o comprueba los permisos de tu carpeta de usuario."; if (App.SmokeTest) FinishSmoke(false, "initialization"); }
    }
    private async Task WaitForScript(string expression)
    {
        for (var attempt = 0; attempt < 150; attempt++)
        {
            if (await Browser.CoreWebView2.ExecuteScriptAsync(expression) == "true") return;
            await Task.Delay(100);
        }
        throw new InvalidOperationException("UI condition timed out.");
    }
    private async Task WaitForPreference(bool enabled)
    {
        for (var attempt = 0; attempt < 50; attempt++) { if (Preferences.Load().OpenFavoritesOnStart == enabled) return; await Task.Delay(100); }
        throw new InvalidOperationException("Preference bridge timed out.");
    }
    private async Task FillSmoke(string name, string value)
    {
        await Browser.CoreWebView2.ExecuteScriptAsync($"(()=>{{const field=document.querySelector('input[name=\"{name}\"]');field.value={JsonSerializer.Serialize(value)};field.dispatchEvent(new Event('input',{{bubbles:true}}));}})()");
    }
    private async Task LoginSmoke(string name, string password, string replacement)
    {
        await WaitForScript("document.querySelector('input[name=\"loginName\"]') !== null");
        await FillSmoke("loginName", name); await FillSmoke("loginPassword", password);
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.auth-card form button[type=submit]').click()");
        await WaitForScript("document.querySelector('h1')?.textContent === 'Cambia tu contraseña'");
        await FillSmoke("initialCurrent", password); await FillSmoke("initialNew", replacement); await FillSmoke("initialRepeat", replacement);
        await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.auth-card form button[type=submit]').click()");
        await WaitForScript("document.querySelector('.priority-grid') !== null");
    }
    private async Task DeniedSmoke(string command, object? payload = null)
    {
        var script = "(()=>{window.smokeReply=null;const id=crypto.randomUUID();const listener=e=>{if(e.data.id===id){window.smokeReply=e.data;chrome.webview.removeEventListener('message',listener);}};chrome.webview.addEventListener('message',listener);chrome.webview.postMessage({id,command:" + JsonSerializer.Serialize(command) + ",payload:" + JsonSerializer.Serialize(payload) + "});})()";
        await Browser.CoreWebView2.ExecuteScriptAsync(script);
        await WaitForScript("window.smokeReply !== null");
        if (await Browser.CoreWebView2.ExecuteScriptAsync("window.smokeReply.ok === false") != "true") throw new InvalidOperationException("Privilege boundary failed: " + command);
    }
    private async Task RunNativeSmoke()
    {
        var stage = "window-startup";
        try
        {
            if (WindowState != WindowState.Maximized || !shell.WithinWorkArea()) throw new InvalidOperationException(shell.Diagnostics());
            stage = "login";
            await LoginSmoke("Viteka", "jupiter", "SmokeSuperPassword1");
            var superId = (await Access.RequireUserAsync()).Id;
            await WaitForScript($"document.querySelector('footer')?.textContent.includes({JsonSerializer.Serialize(typeof(MainWindow).Assembly.GetName().Version?.ToString(3))}) === true");
            await WaitForScript("Math.round(document.querySelector('aside').getBoundingClientRect().width) === 220");
            stage = "window-controls";
            if (Icon is null || !shell.HasNativeIcons || WindowState != WindowState.Maximized || !shell.WithinWorkArea()) throw new InvalidOperationException(shell.Diagnostics());
            MaximizeWindow(this, new RoutedEventArgs()); await Task.Delay(200);
            if (WindowState != WindowState.Normal || !shell.WithinWorkArea()) throw new InvalidOperationException(shell.Diagnostics());
            MaximizeWindow(this, new RoutedEventArgs()); await Task.Delay(200);
            if (WindowState != WindowState.Maximized || !shell.WithinWorkArea()) throw new InvalidOperationException(shell.Diagnostics());
            MaximizeWindow(this, new RoutedEventArgs()); await Task.Delay(200);
            if (WindowState != WindowState.Normal || !shell.WithinWorkArea()) throw new InvalidOperationException(shell.Diagnostics());
            stage = "zoom";
            shell.SmokeCtrlWheel();
            for (var attempt=0;attempt<50&&Math.Round(Browser.ZoomFactor*100)!=110;attempt++) await Task.Delay(100);
            if (Math.Round(Browser.ZoomFactor*100)!=110 || !Equals(ZoomLabel.Content,"110 %") || Preferences.Load().ZoomPercent!=110) throw new InvalidOperationException();
            ResetZoom(this,new RoutedEventArgs());
            stage = "oracle-migration";
            var secret="fixture-"+Guid.NewGuid().ToString("N");var dsn="Viteka_"+Guid.NewGuid().ToString("N")[..16];
            var setup=new NixFarmaConfigurationWindow(null){Owner=this};
            setup.ContentRendered+=(_,_)=>setup.CompleteSmoke(dsn,"fixture-user",secret);
            if(setup.ShowDialog()!=true||setup.Settings is null)throw new InvalidOperationException("Native configuration dialog failed.");
            var legacy=new NixFarmaConnectionStore(store.DirectoryPath);legacy.Save(setup.Settings);
            if(legacy.Load()?.Password!=secret||System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(legacy.FilePath)).Contains(secret))throw new InvalidOperationException("Legacy DPAPI roundtrip failed.");
            var state=await(nixfarma??throw new InvalidOperationException()).HandleAsync("nixfarma.status",lifetime.Token);
            if(JsonSerializer.Serialize(state).Contains(secret)||legacy.Exists)throw new InvalidOperationException("Protected migration failed.");
            var result=await Access.OracleAsync("test");
            if(result.GetProperty("success").GetBoolean()||result.GetProperty("code").GetString()!="dsn_missing")throw new InvalidOperationException("Service ODBC missing-DSN diagnostic failed.");
            var schemaProbe=await Access.UtilityAsync("tenencia.schema");
            if(schemaProbe.GetProperty("code").GetString()!="dsn_missing")throw new InvalidOperationException("Tenencia schema probe must diagnose the missing DSN.");
            await Access.OracleAsync("forget");
            stage="pharmacy-profile";
            var savedPharmacy=await Access.UtilityAsync("pharmacy.save",JsonSerializer.SerializeToElement(new{name="Farmacia de prueba",province="29",town="Málaga",email="fixture@example.test",phone="",address="",postcode="29001"}));
            if(savedPharmacy.GetProperty("profile").GetProperty("province").GetString()!="29")throw new InvalidOperationException("Central pharmacy profile failed.");
            var pendingTenencia=await Access.UtilityAsync("tenencia.query");
            if(pendingTenencia.GetProperty("code").GetString()!="not_configured"||pendingTenencia.GetProperty("rows").GetArrayLength()!=83)throw new InvalidOperationException("Excel groups must remain visible without an Oracle connection.");
            stage="sidebar";
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.menu-toggle').click()");
            await WaitForScript("document.querySelector('.layout')?.classList.contains('collapsed') === true");
            for(var attempt=0;attempt<50&&!Preferences.Load().SidebarCollapsed;attempt++)await Task.Delay(100);
            if(!Preferences.Load().SidebarCollapsed)throw new InvalidOperationException();
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.menu-toggle').click()");
            stage="navigation";
            foreach(var section in new[]{"Aplicaciones","Favoritos","Configuración"})
            {
                await Browser.CoreWebView2.ExecuteScriptAsync($"[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==={JsonSerializer.Serialize(section)}).click()");
                await WaitForScript($"document.querySelector('h1')?.textContent === {JsonSerializer.Serialize(section)}");
            }
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Auditoría').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Auditoría'");
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Configuración').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Configuración'");
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Actualizaciones').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Actualizaciones' && [...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Configuración')?.getAttribute('aria-current') === 'page'");
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Configuración').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Configuración'");
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Usuarios').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Usuarios'");
            stage="tenencia-navigation";
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Aplicaciones').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Aplicaciones'");
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.resource')].find(b=>b.querySelector('strong')?.textContent==='Tenencia obligatoria').click()");
            await WaitForScript("document.querySelector('h1')?.textContent === 'Tenencia obligatoria' && document.querySelectorAll('viteka-tenencia .group-button').length === 83");
            stage="preference";
            await OpenAccountSmoke();
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Pantalla al iniciar').click()");
            await WaitForScript("document.querySelector('[role=dialog] input[type=checkbox]') !== null");
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[type=checkbox]').click();[...document.querySelectorAll('[role=dialog] button')].find(b=>b.textContent.trim()==='Guardar cambios').click()");await WaitForPreference(true);
            Browser.CoreWebView2.Reload();await WaitForScript("document.querySelector('h1')?.textContent === 'Favoritos'");
            await OpenAccountSmoke();
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Pantalla al iniciar').click()");
            await WaitForScript("document.querySelector('input[type=checkbox]')?.checked === true");
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[type=checkbox]').click();[...document.querySelectorAll('[role=dialog] button')].find(b=>b.textContent.trim()==='Guardar cambios').click()");await WaitForPreference(false);
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Inicio').click()");
            await WaitForScript("document.querySelector('.priority-grid') !== null");
            var screenshot=Environment.GetEnvironmentVariable("VITEKA_UI_SMOKE_SCREENSHOT");
            if(!string.IsNullOrEmpty(screenshot)){using var file=File.Create(screenshot);await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,file);}
            stage="normal-user-guards";
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"Cerrar sesión\"]').click()");
            await LoginSmoke("usuario","0000","SmokeUserPassword1");
            await WaitForScript("![...document.querySelectorAll('nav button')].some(b=>b.textContent.includes('Usuarios')||b.textContent.includes('Mi cuenta'))");
            await DeniedSmoke("nixfarma.status");await DeniedSmoke("update.install");
            await DeniedSmoke("tenencia.query");await DeniedSmoke("tenencia.schema");await DeniedSmoke("pharmacy.state");
            foreach(var action in new[]{"tenencia.query","tenencia.schema","pharmacy.state"}){
                try{await Access.UtilityAsync(action);throw new InvalidOperationException("Service accepted a forbidden utility action.");}catch(AccessDeniedException){}
            }
            await DeniedSmoke("users.create",new{name="forbidden",role="SuperAdministrator",password="SmokeForbiddenPassword1",utilityIds=Array.Empty<string>()});
            stage="administrator-guards";
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[aria-label=\"Cerrar sesión\"]').click()");
            await LoginSmoke("F. titular","1234","SmokeAdminPassword1");
            await WaitForScript("[...document.querySelectorAll('nav button')].some(b=>b.textContent.includes('Configuración'))");
            await DeniedSmoke("nixfarma.status");await DeniedSmoke("users.password",new{id=superId,password="SmokeForbiddenPassword1"});
            var sharedPharmacy=await Access.UtilityAsync("pharmacy.state");if(sharedPharmacy.GetProperty("profile").GetProperty("province").GetString()!="29")throw new InvalidOperationException("Pharmacy profile must be shared across accounts.");
            try{await Access.UtilityAsync("tenencia.schema");throw new InvalidOperationException("Titular must not inspect technical metadata.");}catch(AccessDeniedException){}
            FinishSmoke(true,"complete");
        }
        catch(Exception error)
        {
            var detail=error.Message;
            try{detail+="; UI: "+await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[role=alert]')?.textContent||document.querySelector('.error')?.textContent||''");}catch(InvalidOperationException){}
            FinishSmoke(false,stage,detail);
        }
    }
    private async Task OpenAccountSmoke()
    {
        await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Configuración').click()");
        await WaitForScript("document.querySelector('h1')?.textContent === 'Configuración'");
        await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('.setting-row')].find(b=>b.querySelector('strong')?.textContent==='Mi cuenta').click()");
        await WaitForScript("document.querySelector('h1')?.textContent === 'Mi cuenta'");
    }
    private void FinishSmoke(bool passed, string stage, string? error = null)
    {
        var report = Environment.GetEnvironmentVariable("VITEKA_UI_SMOKE_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, JsonSerializer.Serialize(new { passed, stage, error }));
        if (passed) CloseWindow(this, new RoutedEventArgs()); else Application.Current.Shutdown(1);
    }
    private void Post(object message)
    { if (!closing && Browser.CoreWebView2 is {} core) core.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
    private async Task ScheduledUpdateCheck()
    {
        try { if (updates is not null) await updates.CheckAsync(false, lifetime.Token); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or OperationCanceledException or ObjectDisposedException) { }
    }
    private void SynchronizeProfile(object result)
    {
        var document = JsonSerializer.SerializeToElement(result);
        if (!document.TryGetProperty("user", out var user) || user.ValueKind == JsonValueKind.Null) { profile = null; profileId = null; return; }
        var id = user.GetProperty("id").GetString();
        if (id == profileId) return;
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException();
        profile = new LocalStateStore(Path.Combine(store.DirectoryPath, "Profiles", id!)); profileId = id;
        if (!File.Exists(profile.StatePath))
        {
            var prior = store.Load(); profile.SetTheme(prior.Theme); profile.SetSidebar(prior.SidebarCollapsed); profile.SetZoom(prior.ZoomPercent);
            if (user.GetProperty("role").GetString() != "User") profile.SetOpenFavorites(prior.OpenFavoritesOnStart);
        }
        SetZoom(profile.Load().ZoomPercent, false);
    }
    private static bool Trusted(string source) => Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "app.viteka.local" && uri.Port == 443 && uri.UserInfo.Length == 0;
    private void Reply(string id, object? result = null, string? error = null)
    { if (!closing) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { id, ok = error is null, result, error })); }
    private async void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (closing || !Trusted(e.Source) || e.WebMessageAsJson.Length > 8192) return;
        string? id = null;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var identifier) || identifier.ValueKind != JsonValueKind.String || !Guid.TryParse(identifier.GetString(), out _)) return;
            id = identifier.GetString()!;
            var command = root.GetProperty("command").GetString();
            if (command is "auth.state" or "auth.login" or "auth.logout" or "auth.password" or "users.list" or "users.create" or "users.modify" or "users.password" or "utilities.list")
            {
                if (command is "auth.login" or "auth.logout") { profile = null; profileId = null; }
                var result = await Access.HandleAsync(command, root);
                if (command.StartsWith("auth.", StringComparison.Ordinal) || command == "users.modify") SynchronizeProfile(result);
                Reply(id, result); return;
            }
            AccessUserView? actor = null;
            if (command is not ("app.state" or "window.appearance" or "window.zoom")) actor = await Access.RequireUserAsync();
            if(command is "pharmacy.state" or "pharmacy.save" or "tenencia.state" or "tenencia.query" or "tenencia.schema")
            {
                if(actor is null)throw new AccessSessionException();
                if(command.StartsWith("pharmacy.",StringComparison.Ordinal)&&!AccessPolicy.CanManageUsers(actor)||command=="tenencia.schema"&&!AccessPolicy.CanConfigure(actor)||command is "tenencia.state" or "tenencia.query"&&!AccessPolicy.CanUse(actor,Viteka.Contracts.UtilityCatalog.TenenciaId,Viteka.Contracts.UtilityCatalog.Items))throw new AccessDeniedException();
                Reply(id,await Access.UtilityAsync(command,root.TryGetProperty("payload",out var payload)?payload:null));return;
            }
            switch (command)
            {
                case "nixfarma.status":
                case "nixfarma.configure":
                case "nixfarma.test":
                case "nixfarma.forget":
                    if (actor is null || !AccessPolicy.CanConfigure(actor)) throw new AccessDeniedException();
                    if (updates?.IsBusy == true) { Reply(id, error: "Espera a que termine la actualización."); break; }
                    Reply(id, await (nixfarma ?? throw new InvalidOperationException()).HandleAsync(command, lifetime.Token));
                    break;
                case "app.state":
                    var state = Preferences.Load();
                    Reply(id, new { version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3), openFavorites = state.OpenFavoritesOnStart, sidebarCollapsed = state.SidebarCollapsed, theme = state.Theme, zoomPercent = (int)Math.Round(Browser.ZoomFactor * 100) });
                    break;
                case "settings.start":
                    var enabled = root.GetProperty("payload").GetProperty("enabled").GetBoolean();
                    Preferences.SetOpenFavorites(enabled);
                    Reply(id, new { openFavorites = enabled });
                    break;
                case "window.zoom":
                    var percent = root.GetProperty("payload").GetProperty("percent").GetInt32();
                    if (percent is < 50 or > 200) { Reply(id, error: "El zoom debe estar entre 50 y 200 %."); break; }
                    SetZoom(percent);
                    Reply(id, new { percent });
                    break;
                case "settings.sidebar":
                    Preferences.SetSidebar(root.GetProperty("payload").GetProperty("collapsed").GetBoolean());
                    Reply(id, new { saved = true });
                    break;
                case "settings.theme":
                    var theme = root.GetProperty("payload").GetProperty("theme").GetString();
                    if (theme is not ("auto" or "light" or "dark")) { Reply(id, error: "Apariencia no válida."); break; }
                    Preferences.SetTheme(theme);
                    Reply(id, new { saved = true });
                    break;
                case "window.appearance":
                    var dark = root.GetProperty("payload").GetProperty("dark").GetBoolean();
                    Application.Current.Resources["ChromeBackground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#0B1716" : "#FAF6EC"));
                    Application.Current.Resources["ChromeForeground"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dark ? "#F6F7EE" : "#153B35"));
                    Reply(id, new { applied = true });
                    break;
                case "resource.open":
                    var resourceId = root.GetProperty("payload").GetProperty("id").GetString() ?? "";
                    if (resourceId == "anydesk")
                    {
                        var executable = PortalResources.FindAnyDesk();
                        if (executable is null) { Reply(id, new { opened = false }); break; }
                        using var remote = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
                        Reply(id, new { opened = remote is not null });
                    }
                    else
                    {
                        var target = PortalResources.Target(resourceId);
                        if (target is null) { Reply(id, error: "Acceso no disponible."); break; }
                        using var resource = Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                        Reply(id, new { opened = true });
                    }
                    break;
                case "clipboard.copy":
                    var email = PortalResources.Email(root.GetProperty("payload").GetProperty("id").GetString() ?? "");
                    if (email is null) { Reply(id, error: "Correo no disponible."); break; }
                    Clipboard.SetText(email);
                    Reply(id, new { copied = true });
                    break;
                case "update.state":
                case "update.history":
                case "update.check":
                case "update.install":
                case "update.publication":
                    if (command == "update.install" && nixfarma?.IsBusy == true) { Reply(id, error: "Espera a que termine la prueba de conexión antes de actualizar."); break; }
                    Reply(id, await (updates ?? throw new InvalidOperationException()).HandleAsync(command, lifetime.Token));
                    break;
                default: Reply(id, error: "Acción no disponible."); break;
            }
        }
        catch (AccessSessionException) { profile = null; profileId = null; Post(new { type = "session-ended" }); if (id is not null) Reply(id, error: "AUTH_REQUIRED"); }
        catch (AccessDeniedException error) { if (id is not null) Reply(id, error: error.Message); }
        catch (ArgumentException error) { if (id is not null) Reply(id, error: error.Message); }
        catch (HttpRequestException) { if (id is not null) Reply(id, error: "No se pudo contactar con el servicio de usuarios. Revisa que esté iniciado en el servidor y que su identidad esté configurada."); }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { if (id is not null) Reply(id, new { cancelled = true }); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or System.Security.Cryptography.CryptographicException or KeyNotFoundException or HttpRequestException or OperationCanceledException or System.Runtime.InteropServices.ExternalException)
        { if (id is not null) Reply(id, error: "No se pudo completar la operación. La aplicación y tus archivos se conservan."); }
    }
    private void SetZoom(int percent, bool save = true)
    {
        percent = Math.Clamp(percent, 50, 200);
        Browser.ZoomFactor = percent / 100d;
        ZoomLabel.Content = percent + " %";
        if (save) Preferences.SetZoom(percent);
    }
    private void ChangeZoom(int percent)
    {
        try { SetZoom(percent); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException) { MessageBox.Show(this, "El zoom se ha aplicado, pero no se pudo guardar la preferencia.", "Viteka Utilidades"); }
    }
    private void ZoomIn(object sender, RoutedEventArgs e) => ChangeZoom((int)Math.Round(Browser.ZoomFactor * 100) + 10);
    private void ZoomOut(object sender, RoutedEventArgs e) => ChangeZoom((int)Math.Round(Browser.ZoomFactor * 100) - 10);
    private void ResetZoom(object sender, RoutedEventArgs e) => ChangeZoom(100);
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenRuntimeHelp(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true }); }
        catch (Win32Exception) { LoadingMessage.Text = "Instala el runtime Microsoft Edge WebView2 desde la web oficial de Microsoft."; }
    }
}
