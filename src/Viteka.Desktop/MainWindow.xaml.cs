using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.ComponentModel;
using Viteka.Core;
namespace Viteka.Desktop;

public partial class MainWindow : Window
{
    private readonly LocalStateStore store = LocalStateStore.ForCurrentUser();
    private bool initializing = true;
    private Uri? availableRelease;
    private PreviewUpdate? availableUpdate;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HttpClient downloadHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
    private readonly HttpClient releasesHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_048_576 };
    public MainWindow()
    {
        InitializeComponent();
        VersionLabel.Text = "Viteka Utilidades · " + typeof(MainWindow).Assembly.GetName().Version?.ToString(3);
        StorageLocation.Text = store.DirectoryPath;
        try { OpenFavorites.IsChecked = store.Load().OpenFavoritesOnStart; }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        initializing = false;
        SetPage(OpenFavorites.IsChecked == true ? "Favoritos" : "Inicio");
        Closed += (_, _) => { lifetime.Cancel(); releasesHttp.Dispose(); downloadHttp.Dispose(); lifetime.Dispose(); };
    }
    private void Navigate(object sender, RoutedEventArgs e) => SetPage((string)((RadioButton)sender).Tag);
    private void OpenSection(object sender, RoutedEventArgs e) => SetPage((string)((Button)sender).Tag);
    private void SetPage(string page)
    {
        foreach (var nav in NavigationList.Children.OfType<RadioButton>()) nav.IsChecked = (string)nav.Tag == page;
        PageTitle.Text = page == "Inicio" ? "Tu farmacia, más ágil" : page;
        PageSubtitle.Text = page switch {
            "Inicio" => "Todas tus herramientas, en un mismo lugar.",
            "Utilidades" => "Las herramientas de tu farmacia, organizadas para ti.",
            "Favoritos" => "Un acceso directo a lo que más utilizas.",
            "Actividad" => "El historial de tus operaciones, en un solo lugar.",
            _ => "Personaliza tu espacio de trabajo."
        };
        CatalogPanel.Visibility = page == "Configuración" ? Visibility.Collapsed : Visibility.Visible;
        SettingsPanel.Visibility = page == "Configuración" ? Visibility.Visible : Visibility.Collapsed;
        HomeShortcuts.Visibility = page == "Inicio" ? Visibility.Visible : Visibility.Collapsed;
        SearchSurface.Visibility = page == "Actividad" ? Visibility.Collapsed : Visibility.Visible;
        EmptyTitle.Text = page switch {
            "Favoritos" => "Aún no tienes favoritos",
            "Actividad" => "Todavía no hay actividad",
            "Utilidades" => "Aún no hay utilidades disponibles",
            _ => "Tu espacio de trabajo empieza aquí"
        };
        EmptyDescription.Text = page switch {
            "Favoritos" => "Cuando añadamos utilidades, podrás guardar aquí tus accesos habituales.",
            "Actividad" => "Las operaciones aparecerán aquí cuando empecemos a utilizar las herramientas.",
            _ => "Las nuevas utilidades aparecerán en este espacio. Estamos preparando la base de Viteka Utilidades."
        };
        EmptyIcon.Text = page == "Favoritos" ? "\uE734" : page == "Actividad" ? "\uE81C" : "\uE8A5";
    }
    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void MaximizeWindow(object sender, RoutedEventArgs e) { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();
    private static bool IsStorageError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or System.Security.SecurityException;
    private void ShowStorageError() => StorageNotice.Text = "No se pudo guardar o leer la configuración local. Tus archivos se conservan.";
    private void SaveStartPreference(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        var enabled = OpenFavorites.IsChecked == true;
        try { store.SetOpenFavorites(enabled); StorageNotice.Text = ""; }
        catch (Exception ex) when (IsStorageError(ex)) { initializing = true; OpenFavorites.IsChecked = !enabled; initializing = false; ShowStorageError(); }
    }
    private async void CheckUpdates(object sender, RoutedEventArgs e)
    {
        UpdateCheckButton.IsEnabled = false;
        InstallUpdateButton.Visibility = Visibility.Collapsed;
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        availableUpdate = null;
        availableRelease = null;
        UpdateResult.Text = "Buscando versiones de prueba en GitHub…";
        try
        {
            var installed = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            availableUpdate = await new PreviewUpdater(releasesHttp).FindAsync(installed, lifetime.Token);
            availableRelease = availableUpdate?.ReleasePage;
            UpdateResult.Text = availableUpdate is null ? "No hay una versión posterior con un instalador verificable disponible." :
                $"Versión {availableUpdate.Version.ToString(3)} disponible. Puedes instalarla sobre la actual, sin desinstalar ni perder tus preferencias.";
            InstallUpdateButton.Visibility = availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
            OpenReleaseButton.Visibility = availableRelease is null ? Visibility.Collapsed : Visibility.Visible;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or InvalidDataException or JsonException)
        { UpdateResult.Text = "No se pudo comprobar el canal de actualizaciones. Puedes volver a intentarlo."; }
        finally { UpdateCheckButton.IsEnabled = true; }
    }

    private async void InstallUpdate(object sender, RoutedEventArgs e)
    {
        if (availableUpdate is not { } update) return;
        if (MessageBox.Show(this, "Se descargará y verificará el instalador desde GitHub. Es una versión de prueba sin firma digital. Al aceptar la instalación, Viteka se cerrará y Windows podrá pedir permisos de administrador. Tus preferencias se conservarán. ¿Continuar?",
            "Actualizar Viteka Utilidades", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        InstallUpdateButton.IsEnabled = false;
        UpdateCheckButton.IsEnabled = false;
        try
        {
            var progress = new Progress<int>(value => UpdateResult.Text = $"Descargando actualización… {value}%");
            using var installer = await new PreviewUpdater(downloadHttp).DownloadAsync(update, Path.Combine(store.DirectoryPath, "Updates"), progress, lifetime.Token);
            UpdateResult.Text = "Descarga verificada. Abriendo el instalador…";
            // Keep the file guard until Windows has accepted the installer process.
            var process = Process.Start(new ProcessStartInfo(installer.Path) { UseShellExecute = true, Verb = "runas" });
            if (process is null) throw new InvalidOperationException("No se inició el instalador.");
            process.Dispose();
            Application.Current.Shutdown();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { UpdateResult.Text = "Instalación cancelada. La versión actual sigue disponible."; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or OperationCanceledException or Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        { UpdateResult.Text = "No se pudo verificar o iniciar la actualización. No se ha reemplazado la aplicación actual. Vuelve a intentarlo."; }
        finally { InstallUpdateButton.IsEnabled = true; UpdateCheckButton.IsEnabled = true; }
    }

    private void OpenRelease(object sender, RoutedEventArgs e)
    {
        if (availableRelease is null) return;
        try { Process.Start(new ProcessStartInfo(availableRelease.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { UpdateResult.Text = "No se pudo abrir el navegador. Consulta las publicaciones del repositorio Viteka_Utilidades en GitHub."; }
    }

}
