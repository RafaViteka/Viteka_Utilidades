using System.Collections.ObjectModel;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Viteka.Contracts;
using Viteka.Core;
using System.IO;
using System.Text.Json;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Diagnostics;
using System.ComponentModel;

namespace Viteka.Desktop;

public partial class MainWindow : Window
{
    private readonly HashSet<string> favorites = new();
    private readonly LocalStateStore store = LocalStateStore.ForCurrentUser();
    private bool initializing = true;
    private DesktopCredentials? credentials;
    private Uri? availableRelease;
    private readonly HttpClient releasesHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1_048_576 };
    private readonly ObservableCollection<string> activity = new();
    private readonly HttpClient http = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    { BaseAddress = new Uri("http://127.0.0.1:5187"), Timeout = TimeSpan.FromSeconds(5) };
    private string currentPage = "Inicio";

    public MainWindow()
    {
        InitializeComponent();
        ActivityList.ItemsSource = activity;
        StorageLocation.Text = store.DirectoryPath;
        credentials = new DesktopCredentials(store.DirectoryPath);
        try
        {
            var saved = credentials.Load();
            if (saved is not null)
            {
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", saved.Token);
                PairingResult.Text = "Autorización local guardada. Se validará con el servicio al comprobar la conexión.";
            }
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is CryptographicException)
        { PairingResult.Text = "No se pudo recuperar la autorización guardada. Puedes volver a vincular este puesto."; }
        try
        {
            ApplyState(store.Record(LocalAction.DemoStarted));
            OpenFavorites.IsChecked = store.Load().OpenFavoritesOnStart;
        }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); }
        initializing = false;
        if (OpenFavorites.IsChecked == true) SetPage("Favoritos");
        RenderCards();
        Closed += (_, _) => { http.Dispose(); releasesHttp.Dispose(); };
    }

    private void Navigate(object sender, RoutedEventArgs e)
    {
        SetPage((string)((Button)sender).Tag);
    }

    private void SetPage(string page)
    {
        currentPage = page;
        PageTitle.Text = currentPage == "Inicio" ? "Tu farmacia, más ágil" : currentPage;
        PageSubtitle.Text = currentPage == "Favoritos" ? "Tus accesos guardados en este equipo para tu usuario de Windows." : "Viteka Utilidades · Modo demostración";
        CatalogPanel.Visibility = currentPage is "Inicio" or "Utilidades" or "Favoritos" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = currentPage == "Configuración" ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = currentPage == "Actividad" ? Visibility.Visible : Visibility.Collapsed;
        RenderCards();
    }

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        if (UtilityCards is not null) RenderCards();
    }

    private void RenderCards()
    {
        UtilityCards.Children.Clear();
        var query = SearchBox.Text.Trim();
        foreach (var utility in DemoCatalog.Items.Where(x =>
            (currentPage != "Favoritos" || favorites.Contains(x.Id)) &&
            (x.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) || x.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase))))
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "PROPUESTA · " + utility.Category, FontSize = 10, Foreground = Brushes.DimGray });
            panel.Children.Add(new TextBlock { Text = utility.Name, FontSize = 19, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 8), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = utility.Description, TextWrapping = TextWrapping.Wrap, MinHeight = 60 });
            var open = new Button { Content = "Ver propuesta" };
            open.Click += (_, _) =>
            {
                TrySave(() => store.Record(LocalAction.ProposalOpened, utility.Id));
                MessageBox.Show(this, "Esta utilidad todavía no está implementada. No se ha realizado ninguna consulta a NixFarma.", utility.Name, MessageBoxButton.OK, MessageBoxImage.Information);
            };
            panel.Children.Add(open);
            var favorite = new Button { Content = favorites.Contains(utility.Id) ? "Quitar de favoritos" : "Añadir a favoritos" };
            favorite.Click += (_, _) => { TrySave(() => store.SetFavorite(utility.Id, !favorites.Contains(utility.Id))); RenderCards(); };
            panel.Children.Add(favorite);
            UtilityCards.Children.Add(new Border { Child = panel, Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(221, 229, 221)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Margin = new Thickness(0, 0, 14, 14), Width = 245 });
        }
        EmptyCatalog.Text = currentPage == "Favoritos" ? "No hay favoritos que coincidan. Puedes añadirlos desde Utilidades." : "No hay utilidades que coincidan con la búsqueda.";
        EmptyCatalog.Visibility = UtilityCards.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool IsStorageError(Exception ex) => ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or System.Security.SecurityException;

    private void ShowStorageError() => StorageNotice.Text = "No se pudo leer o guardar la configuración local. Los archivos existentes se conservan. Revisa los permisos o contacta con soporte.";

    private bool TrySave(Func<LocalState> update)
    {
        try { ApplyState(update()); StorageNotice.Text = ""; return true; }
        catch (Exception ex) when (IsStorageError(ex)) { ShowStorageError(); return false; }
    }

    private void ApplyState(LocalState state)
    {
        favorites.Clear();
        foreach (var id in state.FavoriteIds) favorites.Add(id);
        activity.Clear();
        foreach (var entry in state.Activity)
        {
            var name = DemoCatalog.Items.FirstOrDefault(x => x.Id == entry.UtilityId)?.Name;
            var text = entry.Action switch
            {
                LocalAction.DemoStarted => "Demostración iniciada; sin consultas a NixFarma",
                LocalAction.ProposalOpened => $"Propuesta abierta: {name}",
                LocalAction.FavoriteAdded => $"Añadida a favoritos: {name}",
                LocalAction.FavoriteRemoved => $"Quitada de favoritos: {name}",
                _ => "Acción local"
            };
            activity.Add($"{entry.At.ToLocalTime():dd/MM/yyyy HH:mm:ss} · {text}");
        }
    }

    private void SaveStartPreference(object sender, RoutedEventArgs e)
    {
        if (initializing) return;
        var enabled = OpenFavorites.IsChecked == true;
        if (!TrySave(() => store.SetOpenFavorites(enabled)))
        {
            initializing = true;
            OpenFavorites.IsChecked = !enabled;
            initializing = false;
        }
    }

    private async void PairDevice(object sender, RoutedEventArgs e)
    {
        PairButton.IsEnabled = false;
        var code = PairingCode.Password.Trim();
        PairingCode.Clear();
        try
        {
            if (code.Length != 32 || !code.All(Uri.IsHexDigit))
            { PairingResult.Text = "Introduce el código temporal de 32 caracteres facilitado por el administrador."; return; }
            using var response = await http.PostAsJsonAsync("pairing/redeem", new PairingRequest(code, Environment.MachineName));
            if (!response.IsSuccessStatusCode)
            { PairingResult.Text = response.StatusCode == HttpStatusCode.Unauthorized ? "Código incorrecto, caducado o ya utilizado." : "No se pudo vincular. Espera un minuto y consulta al administrador."; return; }
            var credential = await response.Content.ReadFromJsonAsync<PairingResponse>();
            if (credential is null || credential.Token.Length != 64 || !credential.Token.All(Uri.IsHexDigit) || credential.ExpiresAt <= DateTimeOffset.UtcNow)
            { PairingResult.Text = "Respuesta de vinculación no válida."; return; }
            credentials!.Save(credential);
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", credential.Token);
            PairingResult.Text = "Puesto vinculado para este usuario de Windows. Oracle sigue sin configurar.";
        }
        catch (Exception ex) when (IsStorageError(ex) || ex is CryptographicException or HttpRequestException or TaskCanceledException or NotSupportedException)
        { PairingResult.Text = "No se pudo completar o guardar la vinculación. Si el código ya se consumió, solicita uno nuevo al administrador."; }
        finally { PairButton.IsEnabled = true; }
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

    private async void CheckService(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        ConnectionResult.Text = "Comprobando…";
        try
        {
            using var response = await http.GetAsync("api/status");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            { ConnectionResult.Text = "Puesto sin autorizar, autorización caducada o revocada. Vincula este usuario con un código nuevo."; return; }
            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<ServiceStatus>();
            if (status is null || status.Mode != "demo" || status.DatabaseConfigured || status.DatabaseStatus != "not_configured")
                ConnectionResult.Text = "Respuesta inesperada del servicio. No se ha validado la conexión a NixFarma.";
            else
                ConnectionResult.Text = "Servicio de demostración disponible. NixFarma sigue sin configurar; no se ha probado Oracle.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or NotSupportedException)
        {
            ConnectionResult.Text = "No se pudo comprobar el servicio local. Revisa que esté arrancado. No se ha probado la conexión a NixFarma.";
        }
        finally { CheckButton.IsEnabled = true; }
    }
}
