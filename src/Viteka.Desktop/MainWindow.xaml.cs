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
    private bool smokeStarted;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    private PreviewUpdate? availableUpdate;
    private bool updateBusy;
    private bool closing;
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        Loaded += async (_, _) => await InitializeBrowser();
        Closed += (_, _) => { closing = true; lifetime.Cancel(); Browser.Dispose(); http.Dispose(); lifetime.Dispose(); };
    }
    private async Task InitializeBrowser()
    {
        try
        {
            var files = Path.Combine(AppContext.BaseDirectory, "Web");
            if (!File.Exists(Path.Combine(files, "index.html"))) throw new FileNotFoundException();
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(store.DirectoryPath, "WebView2"));
            if (closing) return;
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("app.viteka.local", files, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.NavigationStarting += (_, e) => { if (!Trusted(e.Uri)) e.Cancel = true; };
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.WebMessageReceived += ReceiveMessage;
            core.NavigationCompleted += async (_, e) => {
                if (e.IsSuccess) {
                    LoadingPanel.Visibility = Visibility.Collapsed;
                    if (App.SmokeTest && !smokeStarted) { smokeStarted = true; await RunNativeSmoke(); }
                } else { LoadingMessage.Text = "No se pudo cargar la interfaz. Cierra y vuelve a abrir Viteka."; if (App.SmokeTest) FinishSmoke(false, "navigation"); }
            };
            core.ProcessFailed += (_, _) => { LoadingPanel.Visibility = Visibility.Visible; LoadingMessage.Text = "La interfaz se ha detenido. Cierra y vuelve a abrir Viteka."; };
            core.Navigate(Origin + "/index.html?v=" + typeof(MainWindow).Assembly.GetName().Version?.ToString(3));
        }
        catch (WebView2RuntimeNotFoundException)
        { LoadingMessage.Text = "Necesitas el runtime Microsoft Edge WebView2 para abrir la interfaz. Instálalo y vuelve a abrir Viteka."; RuntimeHelp.Visibility = Visibility.Visible; if (App.SmokeTest) FinishSmoke(false, "runtime"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
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
        for (var attempt = 0; attempt < 50; attempt++) { if (store.Load().OpenFavoritesOnStart == enabled) return; await Task.Delay(100); }
        throw new InvalidOperationException("Preference bridge timed out.");
    }
    private async Task RunNativeSmoke()
    {
        var stage = "startup";
        try
        {
            await WaitForScript($"document.querySelector('footer')?.textContent.includes({JsonSerializer.Serialize(typeof(MainWindow).Assembly.GetName().Version?.ToString(3))}) === true");
            await WaitForScript("document.querySelector('.priority-grid') !== null");
            await WaitForScript("Math.round(document.querySelector('aside').getBoundingClientRect().width) === 220");
            stage = "window-controls";
            if (Icon is null) throw new InvalidOperationException();
            MaximizeWindow(this, new RoutedEventArgs());
            await Task.Delay(200);
            if (WindowState != WindowState.Maximized) throw new InvalidOperationException();
            MaximizeWindow(this, new RoutedEventArgs());
            await Task.Delay(200);
            if (WindowState != WindowState.Normal) throw new InvalidOperationException();
            stage = "sidebar";
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.menu-toggle').click()");
            await WaitForScript("document.querySelector('.layout')?.classList.contains('collapsed') === true");
            for (var attempt = 0; attempt < 50 && !store.Load().SidebarCollapsed; attempt++) await Task.Delay(100);
            if (!store.Load().SidebarCollapsed) throw new InvalidOperationException();
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('.menu-toggle').click()");
            await WaitForScript("document.querySelector('.layout')?.classList.contains('collapsed') === false");
            stage = "navigation";
            foreach (var section in new[] { "Utilidades", "Favoritos", "Actividad", "Configuración" })
            {
                await Browser.CoreWebView2.ExecuteScriptAsync($"[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==={JsonSerializer.Serialize(section)}).click()");
                await WaitForScript($"document.querySelector('h1')?.textContent === {JsonSerializer.Serialize(section)}");
            }
            stage = "preference";
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[type=checkbox]').click()");
            await WaitForPreference(true);
            stage = "reload";
            Browser.CoreWebView2.Reload();
            await WaitForScript("document.querySelector('h1')?.textContent === 'Favoritos'");
            stage = "restore";
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Configuración').click()");
            await WaitForScript("document.querySelector('input[type=checkbox]')?.checked === true");
            await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('input[type=checkbox]').click()");
            await WaitForPreference(false);
            await Browser.CoreWebView2.ExecuteScriptAsync("[...document.querySelectorAll('nav button')].find(b=>b.textContent.trim()==='Inicio').click()");
            await WaitForScript("document.querySelector('.priority-grid') !== null");
            stage = "capture";
            var screenshot = Environment.GetEnvironmentVariable("VITEKA_UI_SMOKE_SCREENSHOT");
            if (!string.IsNullOrEmpty(screenshot)) { using var file = File.Create(screenshot); await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, file); }
            FinishSmoke(true, "complete");
        }
        catch { FinishSmoke(false, stage); }
    }
    private void FinishSmoke(bool passed, string stage)
    {
        var report = Environment.GetEnvironmentVariable("VITEKA_UI_SMOKE_REPORT");
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, JsonSerializer.Serialize(new { passed, stage }));
        if (passed) CloseWindow(this, new RoutedEventArgs()); else Application.Current.Shutdown(1);
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
            switch (command)
            {
                case "app.state":
                    var state = store.Load();
                    Reply(id, new { version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3), openFavorites = state.OpenFavoritesOnStart, sidebarCollapsed = state.SidebarCollapsed, theme = state.Theme });
                    break;
                case "settings.start":
                    var enabled = root.GetProperty("payload").GetProperty("enabled").GetBoolean();
                    store.SetOpenFavorites(enabled);
                    Reply(id, new { openFavorites = enabled });
                    break;
                case "settings.sidebar":
                    store.SetSidebar(root.GetProperty("payload").GetProperty("collapsed").GetBoolean());
                    Reply(id, new { saved = true });
                    break;
                case "settings.theme":
                    var theme = root.GetProperty("payload").GetProperty("theme").GetString();
                    if (theme is not ("auto" or "light" or "dark")) { Reply(id, error: "Apariencia no válida."); break; }
                    store.SetTheme(theme);
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
                case "update.check":
                    if (updateBusy) { Reply(id, error: "Ya hay una actualización en curso."); break; }
                    updateBusy = true;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                        timeout.CancelAfter(TimeSpan.FromSeconds(20));
                        availableUpdate = await new PreviewUpdater(http).FindAsync(typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0), timeout.Token);
                        Reply(id, new { available = availableUpdate is not null, version = availableUpdate?.Version.ToString(3) });
                    }
                    finally { updateBusy = false; }
                    break;
                case "update.install":
                    if (updateBusy || availableUpdate is null) { Reply(id, error: "Busca primero una actualización disponible."); break; }
                    updateBusy = true;
                    try { await InstallUpdate(id, availableUpdate); } finally { updateBusy = false; }
                    break;
                case "update.publication":
                    if (availableUpdate is null) { Reply(id, error: "No hay publicación disponible."); break; }
                    Process.Start(new ProcessStartInfo(availableUpdate.ReleasePage.AbsoluteUri) { UseShellExecute = true });
                    Reply(id, new { opened = true });
                    break;
                default: Reply(id, error: "Acción no disponible."); break;
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { if (id is not null) Reply(id, new { cancelled = true }); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or HttpRequestException or OperationCanceledException or System.Runtime.InteropServices.ExternalException)
        { if (id is not null) Reply(id, error: "No se pudo completar la operación. La aplicación y tus archivos se conservan."); }
    }
    private async Task InstallUpdate(string id, PreviewUpdate update)
    {
        if (MessageBox.Show(this, "Se descargará y verificará el instalador desde GitHub. Es una versión de prueba sin firma digital. Viteka se cerrará al iniciar la instalación y Windows podrá pedir permisos. Tus preferencias se conservarán. ¿Continuar?", "Actualizar Viteka Utilidades", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        { Reply(id, new { cancelled = true }); return; }
        var progress = new Progress<int>(percent => { if (!closing) Browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "update-progress", percent })); });
        using var installer = await new PreviewUpdater(http).DownloadAsync(update, Path.Combine(store.DirectoryPath, "Updates"), progress, lifetime.Token);
        var process = Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true, Verb = "runas" });
        if (process is null) throw new InvalidOperationException();
        process.Dispose();
        Reply(id, new { started = true });
        Application.Current.Shutdown();
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private void OpenRuntimeHelp(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://developer.microsoft.com/microsoft-edge/webview2/#download-section") { UseShellExecute = true }); }
        catch (Win32Exception) { LoadingMessage.Text = "Instala el runtime Microsoft Edge WebView2 desde la web oficial de Microsoft."; }
    }
}
