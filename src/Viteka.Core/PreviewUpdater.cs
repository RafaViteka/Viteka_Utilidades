using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace Viteka.Core;

public sealed record PreviewUpdate(Version Version, Uri Installer, string Sha256, long Size, Uri ReleasePage);
public sealed class VerifiedInstaller(string path, FileStream guard) : IDisposable
{
    public string Path { get; } = path;
    public void Dispose() => guard.Dispose();
}

// Development channel only. GitHub attests the digest over HTTPS; this is not an Authenticode signature.
public sealed class PreviewUpdater(HttpClient http)
{
    private const long MaximumSize = 300L * 1024 * 1024;
    private const string Repo = GitHubReleaseChecker.Repository;
    public async Task<PreviewUpdate?> FindAsync(Version installed, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases?per_page=30");
        request.Headers.UserAgent.ParseAdd("Viteka-Utilidades/0.6");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var source = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var block = new byte[8192]; int count;
        while ((count = await source.ReadAsync(block, ct)) > 0)
        {
            if (buffer.Length + count > 1_048_576) throw new InvalidDataException("Respuesta de versiones demasiado grande.");
            await buffer.WriteAsync(block.AsMemory(0, count), ct);
        }
        using var document = JsonDocument.Parse(buffer.ToArray());
        if (document.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Canal de versiones inválido.");
        PreviewUpdate? best = null;
        var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False) continue;
            var tag = Text(release, "tag_name");
            if (tag is null || !Regex.IsMatch(tag, @"^v(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})(-preview\.[1-9][0-9]{0,5})?$", RegexOptions.NonBacktracking)) continue;
            if (!Version.TryParse(tag[1..].Split('-')[0], out var version) || version <= current || best is not null && version <= best.Version) continue;
            var preview = tag.Contains("-preview.", StringComparison.Ordinal);
            var name = $"VitekaUtilidades-Setup-{version.ToString(3)}-win-x64{(preview ? "-preview" : "")}.exe";
            var url = $"https://github.com/{Repo}/releases/download/{Uri.EscapeDataString(tag)}/{name}";
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || Text(asset, "name") != name || Text(asset, "browser_download_url") != url) continue;
                var digest = Text(asset, "digest");
                if (digest is null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$", RegexOptions.NonBacktracking) ||
                    !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out var length) || length is < 2 or > MaximumSize) continue;
                best = new(version, new Uri(url), digest[7..], length, new Uri($"https://github.com/{Repo}/releases/tag/{Uri.EscapeDataString(tag)}"));
                break;
            }
        }
        return best;
    }

    public async Task<VerifiedInstaller> DownloadAsync(PreviewUpdate update, string directory, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        ct = timeout.Token;
        // Revalidate inputs even if callers supply an object not obtained through FindAsync.
        if (update.Installer.Scheme != "https" || update.Installer.Host != "github.com" || !update.Installer.AbsolutePath.StartsWith($"/{Repo}/releases/download/", StringComparison.Ordinal) ||
            update.Installer.Port != 443 || update.Installer.UserInfo.Length != 0 || update.Size is < 2 or > MaximumSize ||
            !Regex.IsMatch(update.Sha256, "^[a-fA-F0-9]{64}$", RegexOptions.NonBacktracking)) throw new InvalidDataException("Paquete de actualización no válido.");
        var staging = System.IO.Path.Combine(directory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        var path = System.IO.Path.Combine(staging, "Viteka-Setup.exe");
        try
        {
            var location = update.Installer;
            HttpResponseMessage? final = null;
            for (var redirects = 0; redirects < 6; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, location);
                request.Headers.UserAgent.ParseAdd("Viteka-Utilidades/0.6");
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    var next = response.Headers.Location;
                    response.Dispose();
                    if (next is null) throw new InvalidDataException("Redirección de descarga inválida.");
                    if (!next.IsAbsoluteUri) next = new Uri(location, next);
                    if (next.Scheme != "https" || next.Port != 443 || next.UserInfo.Length != 0 ||
                        next.Host is not ("github.com" or "release-assets.githubusercontent.com")) throw new InvalidDataException("Destino de descarga no permitido.");
                    location = next;
                    continue;
                }
                final = response;
                break;
            }
            using (var response = final ?? throw new InvalidDataException("Demasiadas redirecciones."))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength is long length && length != update.Size) throw new InvalidDataException("Tamaño de descarga inesperado.");
                using var input = await response.Content.ReadAsStreamAsync(ct);
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var block = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(block, ct)) > 0)
                {
                    total += count;
                    if (total > update.Size) throw new InvalidDataException("La descarga supera el tamaño esperado.");
                    await file.WriteAsync(block.AsMemory(0, count), ct);
                    progress?.Report((int)(total * 100 / update.Size));
                }
                if (total != update.Size) throw new InvalidDataException("Descarga incompleta.");
                file.Flush(true);
            }
            // Reopen read-only and retain a guard against modification until the installer is launched.
            var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var digest = await SHA256.HashDataAsync(guard, ct);
                if (!CryptographicOperations.FixedTimeEquals(digest, Convert.FromHexString(update.Sha256))) throw new InvalidDataException("La verificación SHA-256 no coincide.");
                guard.Position = 0;
                if (guard.ReadByte() != 'M' || guard.ReadByte() != 'Z') throw new InvalidDataException("El paquete no es un ejecutable Windows.");
                return new(path, guard);
            }
            catch { guard.Dispose(); throw; }
        }
        catch { if (File.Exists(path)) File.Delete(path); Directory.Delete(staging); throw; }
    }
    private static string? Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
