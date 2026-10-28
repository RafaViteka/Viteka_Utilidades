using System.Text.Json;
using Viteka.Contracts;
namespace Viteka.Core;

public enum LocalAction { DemoStarted, ProposalOpened, FavoriteAdded, FavoriteRemoved }
public sealed record ActivityEntry(DateTimeOffset At, LocalAction Action, string? UtilityId);
public sealed record LocalState(int SchemaVersion, string[] FavoriteIds, ActivityEntry[] Activity, bool OpenFavoritesOnStart, bool SidebarCollapsed = false, string Theme = "auto", int ZoomPercent = 100)
{
    public static LocalState Empty => new(1, [], [], false);
}

// Only known utility IDs and actions are recorded, never arbitrary text or pharmacy data.
public sealed class LocalStateStore(string directory, IReadOnlyList<UtilityDefinition>? utilities = null)
{
    public string DirectoryPath { get; } = Path.GetFullPath(directory);
    public string StatePath => Path.Combine(DirectoryPath, "state.json");
    private readonly HashSet<string> KnownIds = (utilities ?? UtilityCatalog.Items).Select(x => x.Id).ToHashSet();
    private static readonly HashSet<string> RetiredDemoIds = ["stock", "expiry", "purchases"];
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static LocalStateStore ForCurrentUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Viteka", "Utilidades"));

    public LocalState Load()
    {
        using var transaction = Lock();
        return ReadState();
    }

    public LocalState SetFavorite(string utilityId, bool enabled) => Update(state =>
    {
        RequireKnownId(utilityId);
        var favorites = state.FavoriteIds.ToHashSet();
        if (enabled) favorites.Add(utilityId); else favorites.Remove(utilityId);
        return AddActivity(state with { FavoriteIds = favorites.Order().ToArray() },
            enabled ? LocalAction.FavoriteAdded : LocalAction.FavoriteRemoved, utilityId);
    });
    public LocalState Record(LocalAction action, string? utilityId = null) => Update(state =>
    {
        if (!Enum.IsDefined(action)) throw new ArgumentOutOfRangeException(nameof(action));
        if (action == LocalAction.DemoStarted && utilityId is not null) throw new ArgumentException("Unexpected utility ID.");
        if (action != LocalAction.DemoStarted) RequireKnownId(utilityId);
        return AddActivity(state, action, utilityId);
    });
    public LocalState SetOpenFavorites(bool enabled) => Update(state => state with { OpenFavoritesOnStart = enabled });

    public LocalState SetSidebar(bool collapsed) => Update(state => state with { SidebarCollapsed = collapsed });
    public LocalState SetTheme(string theme) => Update(state => theme is "auto" or "light" or "dark" ? state with { Theme = theme } : throw new ArgumentException("Tema no válido."));

    public LocalState SetZoom(int percent) => Update(state => percent is >= 50 and <= 200 ? state with { ZoomPercent = percent } : throw new ArgumentOutOfRangeException(nameof(percent)));

    private static LocalState AddActivity(LocalState state, LocalAction action, string? id) => state with
    { Activity = new[] { new ActivityEntry(DateTimeOffset.UtcNow, action, id) }.Concat(state.Activity).Take(100).ToArray() };

    private FileStream Lock()
    {
        Directory.CreateDirectory(DirectoryPath);
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(DirectoryPath, "state.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 10) { Thread.Sleep(40); }
        }
    }
    private LocalState ReadState()
    {
        if (!File.Exists(StatePath)) return LocalState.Empty;
        if (new FileInfo(StatePath).Length > 1_048_576) throw new InvalidDataException("El archivo local es demasiado grande.");
        var state = JsonSerializer.Deserialize<LocalState>(File.ReadAllText(StatePath), JsonOptions)
            ?? throw new InvalidDataException("El archivo local está vacío.");
        if (state.SchemaVersion != 1) throw new InvalidDataException("Versión de configuración local no compatible.");
        if (state.FavoriteIds is null || state.Activity is null || state.Activity.Length > 100 ||
            state.FavoriteIds.Any(id => id is null || !(KnownIds.Contains(id) || RetiredDemoIds.Contains(id))) ||
            state.Activity.Any(x => x is null || !Enum.IsDefined(x.Action) ||
                (x.Action == LocalAction.DemoStarted ? x.UtilityId is not null : x.UtilityId is null || !(KnownIds.Contains(x.UtilityId) || RetiredDemoIds.Contains(x.UtilityId)))))
            throw new InvalidDataException("La configuración local no es válida.");
        return state with {
            ZoomPercent = state.ZoomPercent is >= 50 and <= 200 ? state.ZoomPercent : 100,
            Theme = state.Theme is "auto" or "light" or "dark" ? state.Theme : "auto",
            FavoriteIds = state.FavoriteIds.Where(KnownIds.Contains).ToArray(),
            Activity = state.Activity.Where(x => x.UtilityId is null || KnownIds.Contains(x.UtilityId)).ToArray()
        };
    }
    private LocalState Update(Func<LocalState, LocalState> change)
    {
        using var transaction = Lock();
        // Preserve unreadable/corrupt/future state instead of silently replacing it.
        var state = change(ReadState());
        var temporary = Path.Combine(DirectoryPath, $".state-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, state, JsonOptions);
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, StatePath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return state;
    }
    private void RequireKnownId(string? id)
    {
        if (id is null || !KnownIds.Contains(id)) throw new ArgumentException("Utilidad desconocida.", nameof(id));
    }
}
