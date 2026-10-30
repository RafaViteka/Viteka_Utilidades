using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Viteka.Core;
using System.Text.Json;

if (args is ["--download-smoke"])
{
    using var http = new HttpClient(new GitHubSmokeAuthenticationHandler(Environment.GetEnvironmentVariable("VITEKA_SMOKE_GITHUB_TOKEN"), new HttpClientHandler { AllowAutoRedirect = false })) { Timeout = TimeSpan.FromMinutes(10) };
    var checker = new PreviewUpdater(http);
    var destination = Path.Combine(Path.GetTempPath(), "viteka-real-download-" + Guid.NewGuid().ToString("N"));
    try
    {
        var offer = await checker.FindAsync(new Version(0, 0, 0)) ?? throw new Exception("No verifiable preview published");
        using var installer = await checker.DownloadAsync(offer, destination);
        Console.WriteLine("PASS: real GitHub preview downloaded and verified; installer was not executed.");
    }
    finally { if (Directory.Exists(destination)) Directory.Delete(destination, true); }
    return;
}

var root = Path.Combine(Path.GetTempPath(), "viteka-checks-" + Guid.NewGuid().ToString("N"));
var passed = 0;
LocalStateStore Store(string path) => new(path, new[] {
    new Viteka.Contracts.UtilityDefinition("stock", "Fixture", "", "", true),
    new Viteka.Contracts.UtilityDefinition("expiry", "Fixture", "", "", true),
    new Viteka.Contracts.UtilityDefinition("purchases", "Fixture", "", "", true)
});
void Check(string name, Action action) { action(); passed++; Console.WriteLine("PASS: " + name); }
void Require(bool ok) { if (!ok) throw new Exception("Assertion failed"); }
try
{
    var store = Store(Path.Combine(root, "user-a"));
    Check("Favorites and preferences survive reopening", () =>
    {
        store.SetFavorite("stock", true); store.SetOpenFavorites(true);
        var loaded = Store(store.DirectoryPath).Load();
        Require(loaded.FavoriteIds.SequenceEqual(new[] { "stock" }) && loaded.OpenFavoritesOnStart);
        Require(loaded.Activity.Length == 1 && loaded.Activity[0].Action == LocalAction.FavoriteAdded);
    });
    Check("Different user directories remain isolated", () =>
    {
        var other = Store(Path.Combine(root, "user-b"));
        other.SetFavorite("expiry", true);
        Require(!other.Load().FavoriteIds.Contains("stock"));
        Require(!store.Load().FavoriteIds.Contains("expiry"));
    });
    Check("Updates merge across separate store instances", () =>
    {
        Parallel.Invoke(() => Store(store.DirectoryPath).SetFavorite("expiry", true),
                        () => Store(store.DirectoryPath).SetFavorite("purchases", true));
        Require(store.Load().FavoriteIds.Length == 3);
    });
    Check("Removing favorites persists", () =>
    {
        store.SetFavorite("stock", false);
        Require(!Store(store.DirectoryPath).Load().FavoriteIds.Contains("stock"));
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
        var broken = Store(Path.Combine(root, "broken"));
        Directory.CreateDirectory(broken.DirectoryPath); File.WriteAllText(broken.StatePath, "{broken");
        try { broken.SetFavorite("stock", true); throw new Exception("Expected rejection"); }
        catch (JsonException) { }
        Require(File.ReadAllText(broken.StatePath) == "{broken");
    });
    Check("Future schema remains untouched", () =>
    {
        var future = Store(Path.Combine(root, "future"));
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
        tag_name = tag,
        draft = false,
        prerelease = preview,
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
    Check("MVP catalog is empty and retired demos do not reappear", () =>
    {
        Require(Viteka.Contracts.DemoCatalog.Items.Count == 0);
        var legacy = Store(Path.Combine(root, "legacy"));
        legacy.SetFavorite("stock", true);
        var clean = new LocalStateStore(legacy.DirectoryPath).Load();
        Require(clean.FavoriteIds.Length == 0 && clean.Activity.Length == 0);
        // Reading does not destroy the previous file; migration is applied on next save.
        Require(File.ReadAllText(legacy.StatePath).Contains("stock"));
    });
    var installerBody = System.Text.Encoding.UTF8.GetBytes("MZ-verifiable-test-payload-not-executed");
    var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(installerBody)).ToLowerInvariant();
    var assetUrl = "https://github.com/RafaViteka/Viteka_Utilidades/releases/download/v0.6.0-preview.1/VitekaUtilidades-Setup-0.6.0-win-x64-preview.exe";
    var previewJson = JsonSerializer.Serialize(new[] { new { draft = false, prerelease = true, tag_name = "v0.6.0-preview.1",
        assets = new[] { new { name = "VitekaUtilidades-Setup-0.6.0-win-x64-preview.exe", browser_download_url = assetUrl,
            digest = "sha256:" + digest, size = installerBody.Length } } } });
    using var updateHttp = new HttpClient(new DownloadStub(request => request.RequestUri!.Host == "api.github.com"
        ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(previewJson) }
        : new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(installerBody) }));
    var updater = new PreviewUpdater(updateHttp);
    PreviewUpdate? offer = null;
    Check("Preview channel offers later verified installer", () =>
    {
        offer = updater.FindAsync(new Version(0, 5, 0)).GetAwaiter().GetResult();
        Require(offer is not null && offer.Version == new Version(0, 6, 0));
        Require(updater.FindAsync(new Version(0, 6, 0)).GetAwaiter().GetResult() is null);
    });
    Check("Download returns only after matching size and SHA256", () =>
    {
        using var verified = updater.DownloadAsync(offer!, Path.Combine(root, "downloads")).GetAwaiter().GetResult();
        Require(File.ReadAllBytes(verified.Path).SequenceEqual(installerBody));
    });
    Check("Digest mismatch deletes the downloaded executable", () =>
    {
        var destination = Path.Combine(root, "bad-digest");
        try { updater.DownloadAsync(offer! with { Sha256 = new string('0', 64) }, destination).GetAwaiter().GetResult(); throw new Exception("Expected rejection"); }
        catch (InvalidDataException) { }
        Require(!Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Any());
    });
    Check("Wrong length is rejected without retaining an installer", () =>
    {
        var destination = Path.Combine(root, "bad-length");
        try { updater.DownloadAsync(offer! with { Size = installerBody.Length + 1 }, destination).GetAwaiter().GetResult(); throw new Exception("Expected rejection"); }
        catch (InvalidDataException) { }
        Require(!Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories).Any());
    });
    Check("Download never follows a redirect to an unrelated host", () =>
    {
        using var redirectedHttp = new HttpClient(new DownloadStub(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://other.example/install.exe");
            return response;
        }));
        try { new PreviewUpdater(redirectedHttp).DownloadAsync(offer!, Path.Combine(root, "bad-host")).GetAwaiter().GetResult(); throw new Exception("Expected rejection"); }
        catch (InvalidDataException) { }
    });
    Check("Sidebar and appearance persist without changing start preference", () =>
    {
        var local = Store(Path.Combine(root, "appearance"));
        local.SetOpenFavorites(true); local.SetSidebar(true); local.SetTheme("dark");
        var loaded = Store(local.DirectoryPath).Load();
        Require(loaded.OpenFavoritesOnStart && loaded.SidebarCollapsed && loaded.Theme == "dark");
        try { local.SetTheme("arbitrary"); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Require(Store(local.DirectoryPath).Load().Theme == "dark");
    });
    Check("Prior preferences upgrade with appearance defaults", () =>
    {
        var legacy = Store(Path.Combine(root, "old-appearance"));
        Directory.CreateDirectory(legacy.DirectoryPath);
        File.WriteAllText(legacy.StatePath, "{\"SchemaVersion\":1,\"FavoriteIds\":[],\"Activity\":[],\"OpenFavoritesOnStart\":true}");
        var loaded = legacy.Load(); Require(loaded.OpenFavoritesOnStart && !loaded.SidebarCollapsed && loaded.Theme == "auto");
    });
    Check("Portal only resolves approved resource identifiers", () =>
    {
        Require(PortalResources.Target("support") == "https://soporte.viteka.es/");
        Require(PortalResources.Target("manuals") == "https://wiki.nixfarma.es/");
        Require(PortalResources.Target("https://arbitrary.example") is null);
        Require(PortalResources.Target("anydesk") is null); // Native launcher has a separate fixed-path branch, without session arguments.
        Require(PortalResources.Email("support-email") == "soporte@viteka.es" && PortalResources.Email("arbitrary") is null);
    });
    Check("Zoom persists and rejects out-of-range values", () =>
    {
        var local = Store(Path.Combine(root, "zoom")); local.SetZoom(125);
        Require(Store(local.DirectoryPath).Load().ZoomPercent == 125);
        foreach (var invalid in new[] { 49, 201 }) { try { local.SetZoom(invalid); throw new Exception("Expected rejection"); } catch (ArgumentOutOfRangeException) { } }
        Require(Store(local.DirectoryPath).Load().ZoomPercent == 125);
    });
    Check("Old preferences retain a 100 percent zoom default", () =>
    {
        var local = Store(Path.Combine(root, "old-zoom")); Directory.CreateDirectory(local.DirectoryPath);
        File.WriteAllText(local.StatePath, "{\"SchemaVersion\":1,\"FavoriteIds\":[],\"Activity\":[],\"OpenFavoritesOnStart\":false}");
        Require(local.Load().ZoomPercent == 100);
    });
    Check("CI authentication is limited to the GitHub releases API", () =>
    {
        foreach (var (url, expected) in new[] {
            ($"https://api.github.com/repos/{GitHubReleaseChecker.Repository}/releases?per_page=30", true),
            ("https://github.com/example/release.exe", false),
            ("https://release-assets.githubusercontent.com/example.exe", false),
            ("http://api.github.com/repos/example/releases", false)
        })
        {
            using var client = new HttpClient(new GitHubSmokeAuthenticationHandler("test-placeholder", new SmokeAuthenticationProbe(expected)));
            using var response = client.GetAsync(url).GetAwaiter().GetResult();
        }
    });
    Check("Oracle probe enables read-only before its fixed query and rolls back", () =>
    {
        using var connection = new OracleProbeFixture();
        NixFarmaReadProbe.Execute(connection);
        Require(connection.Steps.SequenceEqual(new[] { "open", "begin", "SET TRANSACTION READ ONLY", "SELECT 1 FROM DUAL", "rollback" }));
    });
    Check("Oracle probe never queries when read-only cannot be enabled", () =>
    {
        using var connection = new OracleProbeFixture { FailReadOnly = true };
        try { NixFarmaReadProbe.Execute(connection); throw new Exception("Expected failure"); } catch (ProbeFixtureException) { }
        Require(connection.Steps.SequenceEqual(new[] { "open", "begin", "SET TRANSACTION READ ONLY", "rollback" }));
    });
    Check("Oracle probe rolls back when its query fails", () =>
    {
        using var connection = new OracleProbeFixture { FailQuery = true };
        try { NixFarmaReadProbe.Execute(connection); throw new Exception("Expected failure"); } catch (ProbeFixtureException) { }
        Require(connection.Steps.Last() == "rollback");
    });
    var tenenciaList = new Viteka.Contracts.TenenciaList(1, "29", "fixture-v1", "Datos ficticios de prueba", DateOnly.FromDateTime(DateTime.UtcNow), [new("a", "Grupo ficticio", 3, ["650001", "650002"])]);
    Check("Pharmacy data persists centrally and rejects invalid province and postcode", () =>
    {
        var directory = new PharmacyDirectory(Path.Combine(root, "pharmacy"), b => b, b => b); Require(directory.Load() is null);
        var profile = new Viteka.Contracts.PharmacyProfile("Farmacia ficticia", "29", "Málaga", "prueba@example.test", "", "", "29001"); directory.Save(profile); Require(directory.Load() == profile);
        try { directory.Save(profile with { Province = "../../other" }); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        try { directory.Save(profile with { Postcode = "28001" }); throw new Exception("Expected rejection"); } catch (ArgumentException) { }
        Require(directory.Load() == profile);
    });
    Check("All catalog groups and their medications are available before querying stock", () =>
    {
        var named = tenenciaList with { Groups = [new("a", "Grupo A", 3, ["650001", "650002"]) { Descriptions = new() { { "650001", "Medicamento A1" }, { "650002", "Medicamento A2" } } }, new("b", "Grupo B", 1, ["650003"])] };
        var preview = TenenciaRules.Preview(named); Require(preview.Length == 2 && preview[0].Articles.Length == 2 && preview[1].Articles.Single().Code == "650003"); Require(preview[0].Articles[0].Description == "Medicamento A1" && preview.All(r => r.Status == "not_checked" && r.Stock is null && r.Missing is null));
        Require(preview.SelectMany(r => r.Articles).All(a => a.Stock is null && a.Issue == "not_checked"));
    });
    Check("Reference medicine names survive unavailable NixFarma articles without inventing stock", () =>
    {
        var named = tenenciaList with { Groups = [new("a", "Grupo A", 3, ["650001"]) { Descriptions = new() { { "650001", "Medicamento de referencia" } } }] };
        var row = TenenciaRules.Evaluate(named, new Dictionary<string, Viteka.Contracts.StockArticle>()).Single(); Require(row.Articles.Single().Description == "Medicamento de referencia" && row.Status == "unresolved" && row.Stock is null);
        try { TenenciaRules.Validate(named with { Groups = [new("a", "A", 1, ["650001"]) { Descriptions = new() { { "650099", "No pertenece al grupo" } } }] }); throw new Exception("Expected invalid association"); } catch (InvalidDataException) { }
    });
    Check("The supplied Malaga Excel preserves all83 groups minima duplicate ATCs and source rows", () =>
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Viteka.Server/Catalogs/tenencia")); var list = TenenciaRules.Load(path, "29") ?? throw new Exception("Missing Excel catalogue");
        Require(list.Groups.Length == 83 && list.Groups.Sum(g => g.Minimum) == 178 && list.Groups.Select(g => g.Id).Distinct().Count() == 83);
        Require(list.Groups.Count(g => g.AtcPrefix is not null) == 75 && list.Groups.Count(g => g.ClassificationCode is not null) == 7 && list.Groups.SelectMany(g => g.Codes).Count() == 2089 && list.Groups.SelectMany(g => g.Codes).Distinct().Count() == 2087);
        Require(list.Groups.Count(g => g.AtcPrefix == "C01AA05") == 2 && list.Groups.Single(g => g.SourceRow == 53).RequiresReview && list.Groups.Single(g => g.SourceRow == 84).Minimum == 2 && !list.Groups.Single(g => g.SourceRow == 84).RequiresReview);
        Require(TenenciaRules.Preview(list).Length == 83 && TenenciaRules.Preview(list).All(r => r.Stock is null));
    });
    Check("Video associations preserve explicit routes sanitary products and all first highlighted rows", () =>
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Viteka.Server/Catalogs/tenencia")); var list = TenenciaRules.Load(path, "29")!;
        Require(list.Groups.All(g => g.Codes.Length > 0 && g.Descriptions?.Count == g.Codes.Length));
        var bag = list.Groups.Single(g => g.SourceRow == 84); Require(bag.Codes.Order().SequenceEqual(new[] { "165727", "205500", "337261" }));
        var knife = list.Groups.Single(g => g.SourceRow == 80); Require(knife.Codes.Length == 18 && knife.Codes.Contains("175550") && !knife.Codes.Contains("123299"));
        Require(list.Groups.Single(g => g.SourceRow == 33).Codes.Single() == "971523" && list.Groups.Single(g => g.SourceRow == 59).Codes.Single() == "653109");
        Require(list.Groups.Single(g => g.SourceRow == 53).Codes.Length == 30 && !list.Groups.Single(g => g.SourceRow == 53).Codes.Contains("680610") && list.Groups.Single(g => g.SourceRow == 53).Codes.Contains("679740"));
    });
    Check("All video groups use their fixed references without broadening through ATC", () =>
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Viteka.Server/Catalogs/tenencia")); var list = TenenciaRules.Load(path, "29")!;
        using var c = new TenenciaFixture { AtcColumn = "ATC" }; TenenciaStockQuery.ReadOnly(c, tx => { var resolution = TenenciaArticleResolver.Resolve(c, tx, TenenciaStockQuery.Inspect(c, tx), list, CancellationToken.None); Require(resolution.Groups.Count == 83 && resolution.Groups.Values.Sum(v => v.Length) == 2089 && !c.Steps.Any(s => s.Contains("LIKE ?"))); return true; });
    });
    Check("Per-dose per-size and inconsistent rectal associations remain reviewable", () =>
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Viteka.Server/Catalogs/tenencia")); var list = TenenciaRules.Load(path, "29")!;
        Require(list.Groups.Where(g => g.RequiresReview).Select(g => g.SourceRow!.Value).Order().SequenceEqual(new[] { 53, 61, 83 }));
        var rectal = list.Groups.Single(g => g.SourceRow == 61); Require(rectal.Codes.Contains("718796") && rectal.SelectionNote!.Contains("ampollas"));
        var stocks = list.Groups.SelectMany(g => g.Codes).Distinct().ToDictionary(c => c, c => new Viteka.Contracts.StockArticle(c, "Fixture", 99)); var result = TenenciaRules.Evaluate(list, stocks); Require(result.Single(r => r.SourceRow == 61).Status == "unresolved" && result.Single(r => r.SourceRow == 84).Status == "covered");
    });
    var atcList = tenenciaList with { Groups = [new("a", "ATC A", 3, []) { AtcPrefix = "A01AB" }, new("b", "ATC B", 2, []) { AtcPrefix = "A02B" }, new("c", "Producto sanitario", 4, []) { ClassificationCode = "M02BC", RequiresReview = true }] };
    Check("ATC prefixes resolve exact code associations using parameters without mixing sanitary codes", () =>
    {
        using var c = new TenenciaFixture { AtcColumn = "ATC" }; TenenciaStockQuery.ReadOnly(c, tx => { var schema = TenenciaStockQuery.Inspect(c, tx); var r = TenenciaArticleResolver.Resolve(c, tx, schema, atcList, CancellationToken.None); Require(r.Groups["a"].SequenceEqual(new[] { "650001", "650002" }) && r.Groups["b"].Single() == "650003" && r.Groups["c"].Length == 0); Require(c.ParametersSeen.Contains("A01AB%") && !c.ParametersSeen.Contains("M02BC%")); return true; }); Require(c.Steps.Last() == "rollback");
    });
    Check("Unknown therapeutic fields are identified by unique value coincidences and ambiguity stays unresolved", () =>
    {
        using (var c = new TenenciaFixture { AtcColumn = "GRUPO_TERAPEUTICO" }) { TenenciaStockQuery.ReadOnly(c, tx => { var r = TenenciaArticleResolver.Resolve(c, tx, TenenciaStockQuery.Inspect(c, tx), atcList, CancellationToken.None); Require(r.AtcColumn == "\"GRUPO_TERAPEUTICO\"" && r.Groups["a"].Length == 2); return true; }); }
        using (var c = new TenenciaFixture { AtcColumn = "GRUPO_TERAPEUTICO", TwoAtcCandidates = true }) { TenenciaStockQuery.ReadOnly(c, tx => { var r = TenenciaArticleResolver.Resolve(c, tx, TenenciaStockQuery.Inspect(c, tx), atcList, CancellationToken.None); Require(r.AtcColumn is null && r.Groups["a"].Length == 0 && r.Notes.ContainsKey("a")); return true; }); }
    });
    Check("Route and dose constraints never pass from an ATC aggregate alone", () =>
    {
        var constrained = atcList with { Groups = [atcList.Groups[0] with { RequiresReview = true, SelectionNote = "Solo inyectable" }] }; var mapping = new Dictionary<string, string[]> { { "a", ["650001"] } }; var stocks = new Dictionary<string, Viteka.Contracts.StockArticle> { { "650001", new("650001", "Oral", 99) } }; var row = TenenciaRules.Evaluate(constrained, stocks, mapping, null).Single(); Require(row.Status == "unresolved" && row.Stock is null && row.Articles.Single().Stock == 99 && row.SelectionNote == "Solo inyectable");
    });
    Check("Tenencia groups aggregate only known codes and compute shortfalls", () =>
    {
        var stocks = new Dictionary<string, Viteka.Contracts.StockArticle> { { "650001", new("650001", "A", 1) }, { "650002", new("650002", "B", 1) } };
        var row = TenenciaRules.Evaluate(tenenciaList, stocks).Single(); Require(row.Stock == 2 && row.Missing == 1 && row.Status == "shortfall"); stocks["650001"] = new("650001", "A", 3); Require(TenenciaRules.Evaluate(tenenciaList, stocks).Single().Status == "covered");
    });
    Check("Missing or ambiguous stock never becomes zero or a compliance result", () =>
    {
        var stocks = new Dictionary<string, Viteka.Contracts.StockArticle> { { "650001", new("650001", "A", 10) } };
        var row = TenenciaRules.Evaluate(tenenciaList, stocks).Single(); Require(row.Stock is null && row.Missing is null && row.Status == "unresolved"); stocks["650002"] = new("650002", "B", 5, "ambiguous_stock"); Require(TenenciaRules.Evaluate(tenenciaList, stocks).Single().Status == "unresolved");
    });
    Check("Provincial catalogs fail closed when absent mismatched duplicated or malformed", () =>
    {
        var directory = Path.Combine(root, "catalogs"); Directory.CreateDirectory(directory); Require(TenenciaRules.Load(directory, "29") is null);
        File.WriteAllBytes(Path.Combine(directory, "29.json"), JsonSerializer.SerializeToUtf8Bytes(tenenciaList with { Province = "28" })); try { TenenciaRules.Load(directory, "29"); throw new Exception("Expected mismatch"); } catch (InvalidDataException) { }
        try { TenenciaRules.Validate(tenenciaList with { Groups = [new("a", "A", 0, ["650001"])] }); throw new Exception("Expected invalid minimum"); } catch (InvalidDataException) { }
        try { TenenciaRules.Validate(tenenciaList with { Groups = [new("a", "A", 1, ["650001", "650001"])] }); throw new Exception("Expected duplicate"); } catch (InvalidDataException) { }
    });
    Check("NixFarma stock queries detect columns bind codes and always roll back", () =>
    {
        using var connection = new TenenciaFixture(); var stocks = TenenciaStockQuery.ReadOnly(connection, tx => { var schema = TenenciaStockQuery.Inspect(connection, tx); Require(schema.Stock == "\"STOCK_ACTUAL\""); return TenenciaStockQuery.Read(connection, tx, schema, ["650001", "650002"]); });
        Require(stocks["650001"].Stock == 2 && connection.Steps[1] == "SET TRANSACTION READ ONLY" && connection.Steps.Last() == "rollback" && connection.ParametersSeen.Count == 4);
    });
    Check("Confirmed NixFarma tables read descriptions and current stock by explicit CN without ATC", () =>
    {
        using var c = new TenenciaFixture { NativeSchema = true, StockColumn = "EXI_ACTUALES" };
        var stocks = TenenciaStockQuery.ReadOnly(c, tx => { var schema = TenenciaStockQuery.Inspect(c, tx, nativeNixFarma: true); Require(schema.ArticleTable == "APPUL.AB_ARTICULOS" && schema.StockTable == "APPUL.AB_ARTICULOS_FICHA_E" && schema.Stock == "\"EXI_ACTUALES\""); return TenenciaStockQuery.Read(c, tx, schema, ["650001", "650002"]); });
        Require(stocks["650001"].Description == "Artículo ficticio 650001" && stocks["650001"].Stock == 2 && stocks["650002"].Stock == 1 && c.Steps.Last() == "rollback");
        Require(c.Steps.Any(s => s.Contains("FROM APPUL.AB_ARTICULOS WHERE")) && c.Steps.Any(s => s.Contains("FROM APPUL.AB_ARTICULOS_FICHA_E WHERE")) && !c.Steps.Any(s => s.Contains("LIKE ?")));
    });
    Check("NixFarma stock reading stops before SELECT when read-only fails", () =>
    {
        using var connection = new TenenciaFixture { FailReadOnly = true }; try { TenenciaStockQuery.ReadOnly(connection, tx => TenenciaStockQuery.Inspect(connection, tx)); throw new Exception("Expected failure"); } catch (ProbeFixtureException) { }
        Require(!connection.Steps.Any(s => s.StartsWith("SELECT")) && connection.Steps.Last() == "rollback");
    });
    Check("Alternative ACTUAL is accepted but ambiguous columns and duplicate rows are unresolved", () =>
    {
        using (var connection = new TenenciaFixture { StockColumn = "ACTUAL" }) { TenenciaStockQuery.ReadOnly(connection, tx => { Require(TenenciaStockQuery.Inspect(connection, tx).Stock == "\"ACTUAL\""); return true; }); }
        using (var connection = new TenenciaFixture { AmbiguousColumns = true }) { try { TenenciaStockQuery.ReadOnly(connection, tx => TenenciaStockQuery.Inspect(connection, tx)); throw new Exception("Expected ambiguity"); } catch (InvalidDataException) { } }
        using (var connection = new TenenciaFixture { DuplicateStock = true }) { var stocks = TenenciaStockQuery.ReadOnly(connection, tx => TenenciaStockQuery.Read(connection, tx, TenenciaStockQuery.Inspect(connection, tx), ["650001"])); Require(stocks["650001"].Issue == "ambiguous_stock"); }
    });
    Check("Stock diagnostics distinguish missing article missing row null ambiguous and zero", () =>
    {
        foreach (var item in new[] { (new TenenciaFixture { MissingArticle = true }, "not_found"), (new TenenciaFixture { MissingStock = true }, "stock_not_found"), (new TenenciaFixture { NullStock = true }, "stock_null"), (new TenenciaFixture { DuplicateStock = true }, "ambiguous_stock") })
        {
            using var c = item.Item1; var value = TenenciaStockQuery.ReadOnly(c, tx => TenenciaStockQuery.Read(c, tx, TenenciaStockQuery.Inspect(c, tx), ["650001"]))["650001"]; Require(value.Stock is null && value.Issue == item.Item2);
        }
        using var zero = new TenenciaFixture { ZeroStock = true }; var known = TenenciaStockQuery.ReadOnly(zero, tx => TenenciaStockQuery.Read(zero, tx, TenenciaStockQuery.Inspect(zero, tx), ["650001"]))["650001"]; Require(known.Stock == 0 && known.Issue is null);
    });
    Check("Stock queries reject SQL payloads and unknown utilities remain denied", () =>
    {
        using var connection = new TenenciaFixture(); try { TenenciaStockQuery.ReadOnly(connection, tx => TenenciaStockQuery.Read(connection, tx, new("\"ARTICULO\"", "\"DESCRIPCION\"", "\"ARTICULO\"", "\"ACTUAL\""), ["650001 OR 1=1"])); throw new Exception("Expected invalid code"); } catch (ArgumentException) { }
        Require(!connection.Steps.Any(s => s.Contains("OR 1=1")));
    });
    var accessClock = new AdjustableClock();
    var accessPath = Path.Combine(root, "access-authority");
    var accessCatalog = new[] { new Viteka.Contracts.UtilityDefinition("fixture-a", "A", "", "", true), new Viteka.Contracts.UtilityDefinition("fixture-b", "B", "", "", true) };
    var authority = new AccessDirectory(accessPath, accessCatalog, b => b.ToArray(), b => b.ToArray(), accessClock);
    authority.InitializeDefaults();
    var initialSuper = authority.Login("viteka", "jupiter");
    Check("Bootstrap accounts require a password change and keep remote access closed", () =>
    {
        Require(initialSuper.User.MustChangePassword && !AccessPolicy.CanConfigure(initialSuper.User) && !authority.RemoteAccessReady());
        try { authority.List(initialSuper.Ticket); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
    });
    Check("New passwords accept three characters and reject fewer across create, reset and own change", () =>
    {
        var path = Path.Combine(root, "password-boundaries");
        var a = new AccessDirectory(path, accessCatalog, b => b.ToArray(), b => b.ToArray(), accessClock);
        a.InitializeDefaults(); var initial = a.Login("Viteka", "jupiter");
        try { a.ChangeOwnPassword(initial.Ticket, "jupiter", "ab"); throw new Exception("Accepted short password"); } catch (ArgumentException) { }
        var actor = a.ChangeOwnPassword(initial.Ticket, "jupiter", "abc");
        Require(a.Login("Viteka", "abc").User.Active);
        try { a.Create(actor, "Short", AccessRole.User, "ab", []); throw new Exception("Accepted short creation"); } catch (ArgumentException) { }
        var created = a.Create(actor, "Short", AccessRole.User, "xyz", []);
        try { a.ResetPassword(actor, created.Id, "ab"); throw new Exception("Accepted short reset"); } catch (ArgumentException) { }
        a.ResetPassword(actor, created.Id, "123"); Require(a.Login("Short", "123").User.MustChangePassword);
        Require(AccessPolicy.CanOpenPage(a.Current(actor), "Configuración"));
    });
    var superTicket = authority.ChangeOwnPassword(initialSuper.Ticket, "jupiter", "FixtureSuperPass1");
    var adminInitial = authority.Login("F. titular", "1234");
    var adminTicket = authority.ChangeOwnPassword(adminInitial.Ticket, "1234", "FixtureAdminPass1");
    var normalInitial = authority.Login("usuario", "0000");
    var normalTicket = authority.ChangeOwnPassword(normalInitial.Ticket, "0000", "FixtureUserPass1");
    Check("Remote access opens only after the known bootstrap passwords are replaced", () => Require(authority.RemoteAccessReady()));
    Check("Role matrix reserves technical configuration for Viteka", () =>
    {
        Require(AccessPolicy.CanConfigure(authority.Current(superTicket)));
        Require(!AccessPolicy.CanConfigure(authority.Current(adminTicket)) && !AccessPolicy.CanConfigure(authority.Current(normalTicket)));
        Require(AccessPolicy.CanInstallUpdates(authority.Current(adminTicket)) && !AccessPolicy.CanInstallUpdates(authority.Current(normalTicket)));
        Require(!AccessPolicy.CanOpenPage(authority.Current(normalTicket), "Usuarios"));
        Require(!AccessPolicy.CanUse(authority.Current(normalTicket), Viteka.Contracts.UtilityCatalog.TenenciaId, Viteka.Contracts.UtilityCatalog.Items) && AccessPolicy.CanUse(authority.Current(adminTicket), Viteka.Contracts.UtilityCatalog.TenenciaId, Viteka.Contracts.UtilityCatalog.Items));
        Require(!AccessPolicy.CanOpenPage(authority.Current(normalTicket), "Auditoría") && AccessPolicy.CanOpenPage(authority.Current(adminTicket), "Auditoría") && AccessPolicy.CanOpenPage(authority.Current(superTicket), "Auditoría"));
        Require(AccessPolicy.CanOpenPage(authority.Current(normalTicket), "Configuración") && AccessPolicy.CanOpenPage(authority.Current(adminTicket), "Configuración"));
    });
    Check("Normal users cannot create accounts or reset another password", () =>
    {
        try { authority.Create(normalTicket, "denied", AccessRole.User, "FixtureDeniedPass1", []); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
        try { authority.ResetPassword(normalTicket, initialSuper.User.Id, "FixtureDeniedPass1"); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
    });
    var created = authority.Create(adminTicket, "Personal", AccessRole.User, "FixtureStaffPass1", ["fixture-a"]);
    Check("Titular creates normal users but cannot create or alter privileged accounts", () =>
    {
        foreach (var role in new[] { AccessRole.Administrator, AccessRole.SuperAdministrator })
            try { authority.Create(adminTicket, "denied-role", role, "FixtureDeniedPass1", []); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
        try { authority.ResetPassword(adminTicket, initialSuper.User.Id, "FixtureDeniedPass1"); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
        try { authority.Modify(adminTicket, created.Id, created.Name, AccessRole.Administrator, true, []); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
    });
    var staffInitial = authority.Login("Personal", "FixtureStaffPass1");
    var staffTicket = authority.ChangeOwnPassword(staffInitial.Ticket, "FixtureStaffPass1", "FixtureStaffPass2");
    Check("Application access is an explicit allow-list and unknown utilities are denied", () =>
    {
        var staff = authority.Current(staffTicket);
        Require(AccessPolicy.CanUse(staff, "fixture-a", accessCatalog) && !AccessPolicy.CanUse(staff, "fixture-b", accessCatalog));
        Require(!AccessPolicy.CanUse(staff, "unknown", accessCatalog) && !AccessPolicy.CanUse(authority.Current(superTicket), "unknown", accessCatalog));
        Require(AccessPolicy.CanUse(authority.Current(adminTicket), "fixture-b", accessCatalog));
    });
    Check("Changing grants immediately invalidates the previous session", () =>
    {
        authority.Modify(adminTicket, created.Id, created.Name, AccessRole.User, true, []);
        try { authority.Current(staffTicket); throw new Exception("Expected revocation"); } catch (AccessSessionException) { }
        var refreshed = authority.Login("Personal", "FixtureStaffPass2");
        Require(!AccessPolicy.CanUse(refreshed.User, "fixture-a", accessCatalog));
    });
    Check("Password reset revokes sessions and requires changing the temporary password", () =>
    {
        authority.ResetPassword(adminTicket, normalInitial.User.Id, "FixtureResetPass1");
        try { authority.Current(normalTicket); throw new Exception("Expected revocation"); } catch (AccessSessionException) { }
        var reset = authority.Login("usuario", "FixtureResetPass1");
        Require(reset.User.MustChangePassword);
        normalTicket = authority.ChangeOwnPassword(reset.Ticket, "FixtureResetPass1", "FixtureUserPass2");
    });
    Check("Changing own password requires the current one and revokes other sessions", () =>
    {
        var other = authority.Login("usuario", "FixtureUserPass2");
        try { authority.ChangeOwnPassword(normalTicket, "incorrect", "FixtureUserPass3"); throw new Exception("Expected denial"); } catch (AccessDeniedException) { }
        normalTicket = authority.ChangeOwnPassword(normalTicket, "FixtureUserPass2", "FixtureUserPass3");
        try { authority.Current(other.Ticket); throw new Exception("Expected revocation"); } catch (AccessSessionException) { }
    });
    Check("The last active superadministrator cannot be disabled or demoted", () =>
    {
        try { authority.Modify(superTicket, initialSuper.User.Id, "Viteka", AccessRole.User, true, []); throw new Exception("Expected protection"); } catch (ArgumentException) { }
        try { authority.Modify(superTicket, initialSuper.User.Id, "Viteka", AccessRole.SuperAdministrator, false, []); throw new Exception("Expected protection"); } catch (ArgumentException) { }
        Require(AccessPolicy.CanConfigure(authority.Current(superTicket)));
    });
    Check("Five failed logins lock the account temporarily", () =>
    {
        for (var i = 0; i < 5; i++) try { authority.Login("usuario", "incorrect"); throw new Exception("Expected rejection"); } catch (AccessDeniedException) { }
        try { authority.Login("usuario", "FixtureUserPass3"); throw new Exception("Expected lock"); } catch (AccessDeniedException) { }
        accessClock.Advance(TimeSpan.FromMinutes(3));
        normalTicket = authority.Login("usuario", "FixtureUserPass3").Ticket;
    });
    Check("Disabling an account revokes it and does not preserve an old session after reenabling", () =>
    {
        authority.Modify(adminTicket, normalInitial.User.Id, "usuario", AccessRole.User, false, []);
        try { authority.Current(normalTicket); throw new Exception("Expected revocation"); } catch (AccessSessionException) { }
        authority.Modify(adminTicket, normalInitial.User.Id, "usuario", AccessRole.User, true, []);
        try { authority.Current(normalTicket); throw new Exception("Expected revocation"); } catch (AccessSessionException) { }
    });
    Check("An update preserves passwords and does not recreate bootstrap credentials", () =>
    {
        authority.InitializeDefaults();
        try { authority.Login("Viteka", "jupiter"); throw new Exception("Expected old password rejection"); } catch (AccessDeniedException) { }
        Require(authority.Login("Viteka", "FixtureSuperPass1").User.Role == AccessRole.SuperAdministrator);
    });
    Check("Password verifiers use unique salts and public views contain no verifier", () =>
    {
        var data = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(accessPath, "access.dat")));
        var salts = data.GetProperty("Users").EnumerateArray().Select(u => u.GetProperty("Salt").GetString()).ToArray();
        Require(salts.Distinct().Count() == salts.Length);
        var view = JsonSerializer.Serialize(authority.Current(superTicket));
        Require(!view.Contains("Hash") && !view.Contains("Salt") && !view.Contains("FixtureSuperPass1"));
    });
    Check("Deleting an initialized authority never restores default accounts", () =>
    {
        File.Delete(Path.Combine(accessPath, "access.dat"));
        try { authority.InitializeDefaults(); throw new Exception("Expected preservation"); } catch (InvalidDataException) { }
        Require(!File.Exists(Path.Combine(accessPath, "access.dat")));
    });
    var updateClock = new AdjustableClock();
    var journal = new UpdateJournal(Path.Combine(root, "update-journal"), updateClock);
    Check("Update scheduling persists the 24-hour interval across restarts", () =>
    {
        Require(journal.TryBeginCheck(false)); Require(!journal.TryBeginCheck(true));
        journal.CompleteCheck(new Version(0, 13, 0), true);
        updateClock.Advance(TimeSpan.FromHours(23));
        Require(!new UpdateJournal(Path.Combine(root, "update-journal"), updateClock).TryBeginCheck(false));
        updateClock.Advance(TimeSpan.FromHours(1)); Require(journal.TryBeginCheck(false)); journal.CompleteCheck(null, false);
        Require(!journal.TryBeginCheck(false)); Require(journal.TryBeginCheck(true)); journal.CompleteCheck(new Version(0, 13, 0), true);
    });
    Check("An installer launch is not recorded as an installed or running version", () =>
    {
        journal.RecordRunningVersion(new Version(0, 12, 0)); journal.RecordRunningVersion(new Version(0, 12, 0)); journal.RecordInstallerStarted(new Version(0, 13, 0));
        Require(journal.Load().LastRunVersion == "0.12.0");
        Require(journal.Load().Entries.Count(e => e.Kind == "version_started") == 1);
        journal.RecordRunningVersion(new Version(0, 13, 0));
        Require(journal.Load().LastRunVersion == "0.13.0" && journal.Load().AvailableVersion is null);
    });
    Check("Published update history filters drafts and treats notes as plain text", () =>
    {
        using var client = new HttpClient(new ReleaseStub("[{\"draft\":false,\"tag_name\":\"v0.12.0-preview.1\",\"name\":\"Version\",\"body\":\"<script>untrusted</script>\"},{\"draft\":true,\"tag_name\":\"v9.0.0\"},{\"draft\":false,\"tag_name\":\"../../bad\"}]", System.Net.HttpStatusCode.OK));
        var entries = new PreviewUpdater(client).GetHistoryAsync().GetAwaiter().GetResult();
        Require(entries.Length == 1 && entries[0].Notes == "<script>untrusted</script>" && entries[0].ReleasePage.StartsWith("https://github.com/RafaViteka/", StringComparison.Ordinal));
    });
    Check("Service sessions survive a restart, stay protected and are revalidated against the account", () =>
    {
        byte[] Flip(byte[] b) => b.Select(x => (byte)(x ^ 0x5A)).ToArray();
        var sessionPath = Path.Combine(root, "sessions");
        var hash = new string('a', 64);
        var sessionsStore = new AccessSessionStore(sessionPath, Flip, Flip);
        var live = new PersistedAccessSession(hash, Guid.NewGuid().ToString("N"), "v1", DateTimeOffset.UtcNow.AddHours(1));
        var expired = live with { TokenHash = new string('b', 64), Expires = DateTimeOffset.UtcNow.AddMinutes(-1) };
        sessionsStore.Save([live, expired]);
        var raw = File.ReadAllBytes(Path.Combine(sessionPath, "sessions.dat"));
        Require(!System.Text.Encoding.UTF8.GetString(raw).Contains(hash));
        var loaded = new AccessSessionStore(sessionPath, Flip, Flip).Load();
        Require(loaded.Count == 1 && loaded[0] == live);
        File.WriteAllBytes(Path.Combine(sessionPath, "sessions.dat"), [1, 2, 3]);
        Require(new AccessSessionStore(sessionPath, Flip, Flip).Load().Count == 0);
        var restorePath = Path.Combine(root, "restore-authority");
        var restoreAuthority = new AccessDirectory(restorePath, accessCatalog, b => b.ToArray(), b => b.ToArray());
        restoreAuthority.InitializeDefaults();
        var login = restoreAuthority.Login("usuario", "0000");
        var restored = AccessSessionAgent.Restore(restoreAuthority, login.Ticket, DateTimeOffset.UtcNow.AddHours(1));
        Require(restored.RequireUser(true).Id == login.User.Id && restored.Ticket == login.Ticket);
        restoreAuthority.ChangeOwnPassword(login.Ticket, "0000", "abcd");
        try { restored.RequireUser(true); throw new Exception("Expected the restored session to end after a password change"); } catch (AccessSessionException) { }
        var stale = AccessSessionAgent.Restore(restoreAuthority, restoreAuthority.Login("usuario", "abcd").Ticket, DateTimeOffset.UtcNow.AddMinutes(-1));
        try { stale.RequireUser(true); throw new Exception("Expected an expired restored session to be rejected"); } catch (AccessSessionException) { }
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

sealed class DownloadStub(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization is not null) throw new Exception("Service credentials must never be sent to release hosts");
        return Task.FromResult(response(request));
    }
}

// Only the CI download smoke uses this handler. No token is included in the desktop application.
sealed class GitHubSmokeAuthenticationHandler(string? token, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri;
        if (!string.IsNullOrWhiteSpace(token) && uri is { Scheme: "https", Host: "api.github.com", Port: 443 } &&
            uri.UserInfo.Length == 0 && uri.AbsolutePath == $"/repos/{GitHubReleaseChecker.Repository}/releases")
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return base.SendAsync(request, ct);
    }
}
sealed class SmokeAuthenticationProbe(bool expected) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if ((request.Headers.Authorization?.Scheme == "Bearer") != expected) throw new Exception("Unexpected CI authentication destination.");
        return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}

sealed class ProbeFixtureException : DbException { }
sealed class OracleProbeFixture : DbConnection
{
    public List<string> Steps { get; } = [];
    public bool FailReadOnly { get; init; }
    public bool FailQuery { get; init; }
    private ConnectionState state;
    [AllowNull] public override string ConnectionString { get; set; } = "";
    public override string Database => "Fixture";
    public override string DataSource => "Fixture";
    public override string ServerVersion => "Fixture";
    public override ConnectionState State => state;
    public override void Open() { Steps.Add("open"); state = ConnectionState.Open; }
    public override void Close() { state = ConnectionState.Closed; }
    public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Steps.Add("begin"); return new OracleProbeTransaction(this); }
    protected override DbCommand CreateDbCommand() => new OracleProbeCommand(this);
}
sealed class OracleProbeTransaction(OracleProbeFixture owner) : DbTransaction
{
    public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
    protected override DbConnection DbConnection => owner;
    public override void Commit() => throw new Exception("Probe must never commit.");
    public override void Rollback() => owner.Steps.Add("rollback");
}
sealed class OracleProbeCommand(OracleProbeFixture owner) : DbCommand
{
    [AllowNull] public override string CommandText { get; set; } = "";
    public override int CommandTimeout { get; set; }
    public override CommandType CommandType { get; set; }
    public override bool DesignTimeVisible { get; set; }
    public override UpdateRowSource UpdatedRowSource { get; set; }
    protected override DbConnection? DbConnection { get => owner; set => throw new NotSupportedException(); }
    protected override DbTransaction? DbTransaction { get; set; }
    protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
    public override void Cancel() => throw new NotSupportedException();
    public override void Prepare() => throw new NotSupportedException();
    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
    private void Record()
    {
        if (DbTransaction is null || CommandTimeout != 15) throw new Exception("Unbounded command or missing transaction.");
        owner.Steps.Add(CommandText);
    }
    public override int ExecuteNonQuery()
    {
        Record();
        if (CommandText != "SET TRANSACTION READ ONLY" || owner.FailReadOnly) throw new ProbeFixtureException();
        return -1;
    }
    public override object ExecuteScalar()
    {
        Record();
        if (CommandText != "SELECT 1 FROM DUAL" || owner.FailQuery) throw new ProbeFixtureException();
        return 1;
    }
}
