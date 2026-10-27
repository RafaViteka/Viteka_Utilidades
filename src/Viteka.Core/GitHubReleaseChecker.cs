using System.Net;
using System.Text.Json;
namespace Viteka.Core;

public enum ReleaseCheckStatus { UpToDate, Available, NoStableRelease, MissingInstaller, Unavailable, InvalidResponse }
public sealed record ReleaseCheckResult(ReleaseCheckStatus Status, string? Version = null, Uri? ReleasePage = null);

// Checks only. Never downloads or executes packages, never sends service credentials to GitHub.
public sealed class GitHubReleaseChecker(HttpClient client)
{
    public const string Repository = "RafaViteka/Viteka_Utilidades";
    public async Task<ReleaseCheckResult> CheckAsync(Version installed, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.UserAgent.ParseAdd("Viteka-Utilidades/0.4");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            // A 404 can also mean private repository or unavailable access: do not claim 'up to date'.
            if (!response.IsSuccessStatusCode) return new(ReleaseCheckStatus.Unavailable);
            var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (content.Length > 1_048_576) return new(ReleaseCheckStatus.InvalidResponse);
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(ReleaseCheckStatus.InvalidResponse);
            if (!root.TryGetProperty("draft", out var draft) || !root.TryGetProperty("prerelease", out var prerelease) ||
                draft.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                prerelease.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return new(ReleaseCheckStatus.InvalidResponse);
            if (draft.GetBoolean() || prerelease.GetBoolean()) return new(ReleaseCheckStatus.NoStableRelease);
            var tag = GetString(root, "tag_name");
            var text = tag?.StartsWith('v') == true ? tag[1..] : tag;
            var parts = text?.Split('.');
            if (parts is not { Length: 3 } || parts.Any(x => x.Length is < 1 or > 9 ||
                x.Any(c => c is < '0' or > '9') || x.Length > 1 && x[0] == '0') || !Version.TryParse(text, out var version))
                return new(ReleaseCheckStatus.InvalidResponse);
            if (version <= installed) return new(ReleaseCheckStatus.UpToDate);
            var expectedName = $"VitekaUtilidades-Setup-{version.ToString(3)}-win-x64.exe";
            var encodedTag = Uri.EscapeDataString(tag!);
            var expectedUrl = $"https://github.com/{Repository}/releases/download/{encodedTag}/{expectedName}";
            if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                !assets.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.Object && GetString(x, "name") == expectedName &&
                    GetString(x, "browser_download_url") == expectedUrl))
                return new(ReleaseCheckStatus.MissingInstaller, version.ToString(3));
            return new(ReleaseCheckStatus.Available, version.ToString(3),
                new Uri($"https://github.com/{Repository}/releases/tag/{encodedTag}"));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new(ReleaseCheckStatus.Unavailable); }
        catch (HttpRequestException) { return new(ReleaseCheckStatus.Unavailable); }
        catch (JsonException) { return new(ReleaseCheckStatus.InvalidResponse); }
    }
    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
