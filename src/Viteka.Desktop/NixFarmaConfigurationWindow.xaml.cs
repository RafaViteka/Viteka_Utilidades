using System.IO;
using System.Windows;

namespace Viteka.Desktop;

internal partial class NixFarmaConfigurationWindow : Window
{
    private readonly NixFarmaSettings? prior;
    internal NixFarmaSettings? Settings { get; private set; }
    internal NixFarmaConfigurationWindow(NixFarmaSettings? settings)
    {
        InitializeComponent();
        prior = settings;
        DsnInput.Text = settings?.Dsn ?? "NIXFARMA";
        UserInput.Text = settings?.User ?? "";
        PasswordHint.Text = settings is null ? "Introduce la contraseña de la cuenta Oracle." : "Déjala vacía para conservarla si no cambias el DSN ni el usuario.";
        Loaded += (_, _) => { if (string.IsNullOrEmpty(UserInput.Text)) UserInput.Focus(); else PasswordInput.Focus(); };
    }
    private void SaveConnection(object sender, RoutedEventArgs e)
    {
        var dsn = DsnInput.Text.Trim(); var user = UserInput.Text.Trim();
        var password = PasswordInput.Password;
        if (password.Length == 0 && prior?.Dsn == dsn && prior.User == user) password = prior.Password;
        var settings = new NixFarmaSettings { Dsn = dsn, User = user, Password = password };
        try { settings.Validate(); }
        catch (InvalidDataException) { ValidationMessage.Text = "Indica el DSN, el usuario y la contraseña de Oracle. Si cambias DSN o usuario, vuelve a introducir la contraseña."; return; }
        Settings = settings;
        PasswordInput.Clear();
        DialogResult = true;
    }
    internal void CompleteSmoke(string dsn, string user, string password)
    {
        if (!App.SmokeTest) throw new InvalidOperationException();
        DsnInput.Text = dsn; UserInput.Text = user; PasswordInput.Password = password;
        SaveConnection(this, new RoutedEventArgs());
    }
}
