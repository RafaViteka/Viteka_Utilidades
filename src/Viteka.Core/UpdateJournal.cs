using System.Text.Json;

namespace Viteka.Core;

public sealed record UpdateJournalEntry(DateTimeOffset At, string Kind, string Version);
public sealed record UpdateJournalState(int Schema, DateTimeOffset? LastAttempt, DateTimeOffset? LastSuccess, DateTimeOffset? Pending, string? AvailableVersion, string? LastRunVersion, UpdateJournalEntry[] Entries)
{
    public static UpdateJournalState Empty => new(1, null, null, null, null, null, []);
}

// Runtime evidence, not installation claims: only an actual launch records a running version.
public sealed class UpdateJournal(string directory, TimeProvider? clock = null)
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private string FilePath => Path.Combine(directory, "update-history.json");
    public UpdateJournalState Load() { using var guard = Lock(); return Read(); }
    public bool TryBeginCheck(bool manual)
    {
        using var guard = Lock(); var state = Read(); var now = time.GetUtcNow();
        if (state.Pending is {} pending && pending <= now && now - pending < TimeSpan.FromMinutes(1)) return false;
        if (!manual && state.LastAttempt is {} last && last <= now.AddMinutes(5) && now - last < TimeSpan.FromHours(24)) return false;
        Write(state with { LastAttempt = now, Pending = now }); return true;
    }
    public void CompleteCheck(Version? available, bool success)
    {
        using var guard = Lock(); var state = Read(); var now = time.GetUtcNow();
        var next = state with { Pending = null, LastSuccess = success ? now : state.LastSuccess,
            AvailableVersion = success ? available?.ToString(3) : state.AvailableVersion };
        Write(Add(next, success ? "check_succeeded" : "check_failed", available?.ToString(3) ?? ""));
    }
    public void RecordRunningVersion(Version version)
    {
        using var guard = Lock(); var state = Read(); var value = version.ToString(3);
        if (state.LastRunVersion == value) return;
        Write(Add(state with { LastRunVersion = value, AvailableVersion = Version.TryParse(state.AvailableVersion, out var offer) && offer > version ? state.AvailableVersion : null }, "version_started", value));
    }
    public void RecordInstallerStarted(Version version)
    {
        using var guard = Lock(); Write(Add(Read(), "installer_started", version.ToString(3)));
    }
    private UpdateJournalState Add(UpdateJournalState state, string kind, string version) => state with
    { Entries = new[] { new UpdateJournalEntry(time.GetUtcNow(), kind, version) }.Concat(state.Entries).Take(100).ToArray() };
    private FileStream Lock()
    {
        Directory.CreateDirectory(directory);
        for (var attempt = 0; ; attempt++)
        {
            try { return new FileStream(Path.Combine(directory, "update-history.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 30) { Thread.Sleep(40); }
        }
    }
    private UpdateJournalState Read()
    {
        if (!File.Exists(FilePath)) return UpdateJournalState.Empty;
        if (new FileInfo(FilePath).Length > 1_048_576) throw new InvalidDataException();
        var state = JsonSerializer.Deserialize<UpdateJournalState>(File.ReadAllBytes(FilePath)) ?? throw new InvalidDataException();
        if (state.Schema != 1 || state.Entries is null || state.Entries.Length > 100 ||
            state.AvailableVersion is not null && !Version.TryParse(state.AvailableVersion, out _) ||
            state.LastRunVersion is not null && !Version.TryParse(state.LastRunVersion, out _) ||
            state.Entries.Any(e => e is null || e.Kind is not ("check_succeeded" or "check_failed" or "version_started" or "installer_started") ||
                e.Version is null || e.Version != "" && !Version.TryParse(e.Version, out _))) throw new InvalidDataException();
        return state;
    }
    private void Write(UpdateJournalState state)
    {
        var temporary = Path.Combine(directory, $".update-history-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(file, state); file.Flush(true); }
            File.Move(temporary, FilePath, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
