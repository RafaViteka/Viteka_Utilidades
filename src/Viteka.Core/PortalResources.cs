namespace Viteka.Core;

// Identifiers are accepted from the UI; arbitrary URLs, arguments and executable paths are not.
public static class PortalResources
{
    private static readonly IReadOnlyDictionary<string, string> Targets = new Dictionary<string, string>
    {
        ["support"] = "https://soporte.viteka.es/",
        ["manuals"] = "https://wiki.nixfarma.es/",
        ["training"] = "https://www.youtube.com/@viteka_formacion",
        ["website"] = "https://viteka.es/",
        ["phone"] = "tel:+34951552730",
        ["support-email"] = "mailto:soporte@viteka.es",
        ["info-email"] = "mailto:info@viteka.es",
        ["anydesk-download"] = "https://anydesk.com/es/downloads/windows"
    };
    public static string? Target(string id) => Targets.GetValueOrDefault(id);
    public static string? Email(string id) => id switch { "support-email" => "soporte@viteka.es", "info-email" => "info@viteka.es", _ => null };
    public static string? FindAnyDesk()
    {
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            var path = Path.Combine(folder, "AnyDesk", "AnyDesk.exe");
            if (File.Exists(path)) return path;
        }
        return null;
    }
}
