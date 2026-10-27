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
        Closed += (_, _) => releasesHttp.Dispose();
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
        OpenReleaseButton.Visibility = Visibility.Collapsed;
        availableRelease = null;
        UpdateResult.Text = "Consultando publicaciones de GitHub…";
        try
        {
            var installed = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0, 0);
            var result = await new GitHubReleaseChecker(releasesHttp).CheckAsync(installed);
            UpdateResult.Text = result.Status switch
            {
                ReleaseCheckStatus.Available => $"Versión {result.Version} disponible. Revisa la publicación con el administrador. No se descargará ni instalará automáticamente.",
                ReleaseCheckStatus.UpToDate => "No hay una versión estable posterior a la instalada.",
                ReleaseCheckStatus.NoStableRelease => "La publicación recibida es de prueba; no se ofrece como actualización estable.",
                ReleaseCheckStatus.MissingInstaller => $"La versión {result.Version} no incluye un instalador Windows x64 compatible. No se ofrece para actualizar.",
                ReleaseCheckStatus.InvalidResponse => "La publicación no tiene el formato esperado. No se ofrece ninguna actualización.",
                _ => "No se pudo comprobar el canal. Puede faltar una publicación estable o el repositorio puede requerir acceso. No significa que estés actualizado."
            };
            availableRelease = result.ReleasePage;
            OpenReleaseButton.Visibility = availableRelease is null ? Visibility.Collapsed : Visibility.Visible;
        }
        finally { UpdateCheckButton.IsEnabled = true; }
    }

    private void OpenRelease(object sender, RoutedEventArgs e)
    {
        if (availableRelease is null) return;
        try { Process.Start(new ProcessStartInfo(availableRelease.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { UpdateResult.Text = "No se pudo abrir el navegador. Consulta las publicaciones del repositorio Viteka_Utilidades en GitHub."; }
    }

}
