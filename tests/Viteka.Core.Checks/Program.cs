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
        using var redirectedHttp = new HttpClient(new DownloadStub(_ => {
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
        local.SetOpenFavorites(true);local.SetSidebar(true);local.SetTheme("dark");
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
        var loaded=legacy.Load();Require(loaded.OpenFavoritesOnStart && !loaded.SidebarCollapsed && loaded.Theme=="auto");
    });
    Check("Portal only resolves approved resource identifiers", () =>
    {
        Require(PortalResources.Target("support")=="https://soporte.viteka.es/");
        Require(PortalResources.Target("manuals")=="https://wiki.nixfarma.es/");
        Require(PortalResources.Target("https://arbitrary.example") is null);
        Require(PortalResources.Target("anydesk") is null); // Native launcher has a separate fixed-path branch, without session arguments.
        Require(PortalResources.Email("support-email")=="soporte@viteka.es" && PortalResources.Email("arbitrary") is null);
    });
    Check("Zoom persists and rejects out-of-range values", () =>
    {
        var local=Store(Path.Combine(root,"zoom"));local.SetZoom(125);
        Require(Store(local.DirectoryPath).Load().ZoomPercent==125);
        foreach(var invalid in new[]{49,201}) {try{local.SetZoom(invalid);throw new Exception("Expected rejection");}catch(ArgumentOutOfRangeException){}}
        Require(Store(local.DirectoryPath).Load().ZoomPercent==125);
    });
    Check("Old preferences retain a 100 percent zoom default", () =>
    {
        var local=Store(Path.Combine(root,"old-zoom"));Directory.CreateDirectory(local.DirectoryPath);
        File.WriteAllText(local.StatePath,"{\"SchemaVersion\":1,\"FavoriteIds\":[],\"Activity\":[],\"OpenFavoritesOnStart\":false}");
        Require(local.Load().ZoomPercent==100);
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
