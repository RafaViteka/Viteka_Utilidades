using Viteka.Core;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "viteka-checks-" + Guid.NewGuid().ToString("N"));
var passed = 0;
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
void Require(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
try
{
    var store = new LocalStateStore(Path.Combine(root, "user-a"));
    Check("Favorites and preferences survive reopening", () =>
    {
        store.SetFavorite("stock", true); store.SetOpenFavorites(true);
        var loaded = new LocalStateStore(store.DirectoryPath).Load();
        Require(loaded.FavoriteIds.SequenceEqual(new[] { "stock" }) && loaded.OpenFavoritesOnStart);
        Require(loaded.Activity.Length == 1 && loaded.Activity[0].Action == LocalAction.FavoriteAdded);
    });
    Check("Different user directories remain isolated", () =>
    {
        var other = new LocalStateStore(Path.Combine(root, "user-b"));
        other.SetFavorite("expiry", true);
        Require(!other.Load().FavoriteIds.Contains("stock"));
        Require(!store.Load().FavoriteIds.Contains("expiry"));
    });
    Check("Updates merge across separate store instances", () =>
    {
        Parallel.Invoke(() => new LocalStateStore(store.DirectoryPath).SetFavorite("expiry", true),
                        () => new LocalStateStore(store.DirectoryPath).SetFavorite("purchases", true));
        Require(store.Load().FavoriteIds.Length == 3);
    });
    Check("Removing favorites persists", () =>
    {
        store.SetFavorite("stock", false);
        Require(!new LocalStateStore(store.DirectoryPath).Load().FavoriteIds.Contains("stock"));
    });
    Check("Activity retained at most 100 entries", () =>
    {
        for (var i = 0; i < 105; i++) store.Record(LocalAction.ProposalOpened, "expiry");
        Require(store.Load().Activity.Length == 100);
    });
    Check("Unknown utility cannot enter storage", () =>
    {
        var before = File.ReadAllText(store.StatePath);
        try { store.SetFavorite("arbitrary-sensitive-text", true); throw new Exception("Expected rejection"); }
        catch (ArgumentException) { }
        Require(File.ReadAllText(store.StatePath) == before);
    });
    Check("Corrupt state remains untouched", () =>
    {
        var broken = new LocalStateStore(Path.Combine(root, "broken"));
        Directory.CreateDirectory(broken.DirectoryPath); File.WriteAllText(broken.StatePath, "{broken");
        try { broken.SetFavorite("stock", true); throw new Exception("Expected rejection"); }
        catch (JsonException) { }
        Require(File.ReadAllText(broken.StatePath) == "{broken");
    });
    Check("Future schema remains untouched", () =>
    {
        var future = new LocalStateStore(Path.Combine(root, "future"));
        Directory.CreateDirectory(future.DirectoryPath);
        const string content = "{\"SchemaVersion\":2,\"FavoriteIds\":[],\"Activity\":[]}";
        File.WriteAllText(future.StatePath, content);
        try { future.SetOpenFavorites(true); throw new Exception("Expected rejection"); }
        catch (InvalidDataException) { }
        Require(File.ReadAllText(future.StatePath) == content);
    });
    var clock = new AdjustableClock();
    var registryDir = Path.Combine(root, "registry");
    var registry = new DeviceRegistry(registryDir, clock);
    string? code = null;
    IssuedCredential? credential = null;
    Check("Pairing requires a valid code and consumes it once", () =>
    {
        Require(!registry.IsAuthorized(new string('0', 64)));
        code = registry.IssueCode();
        Require(registry.Redeem(new string('0', 32), "TEST-PC") is null);
        credential = registry.Redeem(code, "TEST-PC");
        Require(credential is not null && registry.IsAuthorized(credential.Token));
        Require(registry.Redeem(code, "OTHER-PC") is null);
    });
    Check("Registry stores hashes, not raw tokens or codes", () =>
    {
        var json = File.ReadAllText(Path.Combine(registryDir, "devices.json"));
        Require(!json.Contains(credential!.Token) && !json.Contains(code!));
        Require(new DeviceRegistry(registryDir, clock).IsAuthorized(credential.Token));
    });
    Check("Revocation immediately invalidates a credential", () =>
    {
        Require(new DeviceRegistry(registryDir, clock).Revoke(credential!.DeviceId));
        Require(!registry.IsAuthorized(credential!.Token));
    });
    Check("Expired pairing code cannot be redeemed", () =>
    {
        var expired = registry.IssueCode(); clock.Advance(TimeSpan.FromMinutes(6));
        Require(registry.Redeem(expired, "TEST-PC") is null);
    });
    Check("Replacement invalidates previous pairing code", () =>
    {
        var previous = registry.IssueCode(); var replacement = registry.IssueCode();
        Require(registry.Redeem(previous, "TEST-PC") is null);
        Require(registry.Redeem(replacement, "TEST-PC") is not null);
    });
    Check("Parallel redemptions produce only one credential", () =>
    {
        var once = registry.IssueCode();
        var results = new System.Collections.Concurrent.ConcurrentBag<IssuedCredential?>();
        Parallel.Invoke(() => results.Add(registry.Redeem(once, "PC-A")), () => results.Add(registry.Redeem(once, "PC-B")));
        Require(results.Count(x => x is not null) == 1);
    });
    Check("Device credentials expire", () =>
    {
        var valid = registry.Redeem(registry.IssueCode(), "TEST-PC")!;
        Require(registry.IsAuthorized(valid.Token));
        clock.Advance(TimeSpan.FromDays(91));
        Require(!registry.IsAuthorized(valid.Token));
    });
    ReleaseCheckResult CheckRelease(string json, System.Net.HttpStatusCode status = System.Net.HttpStatusCode.OK)
    {
        using var http = new HttpClient(new ReleaseStub(json, status));
        return new GitHubReleaseChecker(http).CheckAsync(new Version(0, 4, 0)).GetAwaiter().GetResult();
    }
    string ReleaseJson(string tag, bool preview = false, string? url = null, bool installer = true) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft = false, prerelease = preview,
        assets = installer ? new[] { new { name = "VitekaUtilidades-Setup-0.5.0-win-x64.exe", browser_download_url = url ??
            "https://github.com/RafaViteka/Viteka_Utilidades/releases/download/v0.5.0/VitekaUtilidades-Setup-0.5.0-win-x64.exe" } } : []
    });
    Check("Stable newer GitHub release yields review link only", () =>
    {
        var result = CheckRelease(ReleaseJson("v0.5.0"));
        Require(result.Status == ReleaseCheckStatus.Available && result.Version == "0.5.0");
        Require(result.ReleasePage?.AbsoluteUri == "https://github.com/RafaViteka/Viteka_Utilidades/releases/tag/v0.5.0");
    });
    Check("Same or older release is not an upgrade", () =>
    {
        Require(CheckRelease(ReleaseJson("v0.4.0")).Status == ReleaseCheckStatus.UpToDate);
        Require(CheckRelease(ReleaseJson("v0.3.0")).Status == ReleaseCheckStatus.UpToDate);
    });
    Check("Prerelease is not offered as stable", () =>
        Require(CheckRelease(ReleaseJson("v0.5.0", preview: true)).Status == ReleaseCheckStatus.NoStableRelease));
    Check("Missing or external installer is not offered", () =>
    {
        Require(CheckRelease(ReleaseJson("v0.5.0", installer: false)).Status == ReleaseCheckStatus.MissingInstaller);
        var redirected = CheckRelease(ReleaseJson("v0.5.0", url: "https://other.example/installer.exe"));
        Require(redirected.Status == ReleaseCheckStatus.MissingInstaller && redirected.ReleasePage is null);
    });
    Check("Inaccessible release is not treated as up-to-date", () =>
    {
        Require(CheckRelease("{}", System.Net.HttpStatusCode.NotFound).Status == ReleaseCheckStatus.Unavailable);
        Require(CheckRelease("{}", System.Net.HttpStatusCode.Forbidden).Status == ReleaseCheckStatus.Unavailable);
    });
    Check("Malformed release data is rejected", () =>
    {
        Require(CheckRelease("broken json").Status == ReleaseCheckStatus.InvalidResponse);
        Require(CheckRelease("[]").Status == ReleaseCheckStatus.InvalidResponse);
        Require(CheckRelease(ReleaseJson("v0.5.0-preview")).Status == ReleaseCheckStatus.InvalidResponse);
    });
    Console.WriteLine($"{passed} checks passed.");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

sealed class AdjustableClock : TimeProvider
{
    private DateTimeOffset current = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => current;
    public void Advance(TimeSpan elapsed) => current += elapsed;
}

sealed class ReleaseStub(string content, System.Net.HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri?.Host != "api.github.com" || request.Headers.Authorization is not null || !request.Headers.UserAgent.Any())
            throw new Exception("Unexpected destination, credentials or missing User-Agent");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
    }
}
