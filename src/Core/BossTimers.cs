namespace MapleHelper.Core;

public sealed record BossTimer(Guid Id, int BossId, string BossName, int MapId, string MapName,
    int Channel, int DurationSeconds, DateTimeOffset DueAt, bool Notified = false);
public sealed record BossTimerDuration(int BossId, int MapId, int Seconds);
public sealed class BossTimerState {
    public List<BossTimer> Timers { get; set; } = [];
    public List<BossTimerDuration> Durations { get; set; } = [];
}

// All mutations commit to disk before replacing state, including notification receipts.
public sealed class BossTimers {
    private readonly string path;
    private BossTimerState state = new();
    public IReadOnlyList<BossTimer> Timers => state.Timers.AsReadOnly();
    public bool LoadFailed { get; private set; }
    public BossTimers(string path) {
        this.path = path;
        if (!File.Exists(path)) return;
        var loaded = AtomicJson.Read<BossTimerState>(path);
        if (loaded?.Timers == null || loaded.Durations == null ||
            loaded.Timers.Any(t => t == null || t.Id == Guid.Empty || t.Channel <= 0 || t.DurationSeconds <= 0 || t.BossName == null || t.MapName == null) ||
            loaded.Durations.Any(d => d == null || d.Seconds <= 0) ||
            loaded.Timers.Select(t => t.Id).Distinct().Count() != loaded.Timers.Count ||
            loaded.Timers.Select(t => (t.BossId, t.MapId, t.Channel)).Distinct().Count() != loaded.Timers.Count) {
            LoadFailed = true; return;
        }
        state = loaded;
    }
    public int? RememberedSeconds(int bossId, int mapId) => state.Durations.LastOrDefault(d => d.BossId == bossId && d.MapId == mapId)?.Seconds;
    public BossTimer Start(int bossId, string bossName, int mapId, string mapName, int channel, int seconds, DateTimeOffset now, Guid? editing = null) {
        if (channel <= 0) throw new ArgumentOutOfRangeException(nameof(channel));
        if (seconds <= 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        var previous = state.Timers.FirstOrDefault(t => t.BossId == bossId && t.MapId == mapId && t.Channel == channel);
        var timer = new BossTimer(previous?.Id ?? editing ?? Guid.NewGuid(), bossId, bossName, mapId, mapName, channel, seconds, now.ToUniversalTime().AddSeconds(seconds));
        Commit(new() {
            Timers = state.Timers.Where(t => t.Id != timer.Id && t.Id != editing).Append(timer).ToList(),
            Durations = state.Durations.Where(d => d.BossId != bossId || d.MapId != mapId).Append(new(bossId, mapId, seconds)).ToList()
        });
        return timer;
    }
    public void Remove(Guid id) => Commit(new() { Timers = state.Timers.Where(t => t.Id != id).ToList(), Durations = state.Durations });
    // O(N) in both sparse and dense expiry cases; allocate only when a deadline is newly due.
    public IReadOnlyList<BossTimer> TakeDue(DateTimeOffset now) {
        List<BossTimer>? due = null;
        foreach (var timer in state.Timers) if (!timer.Notified && timer.DueAt <= now) (due ??= []).Add(timer);
        if (due == null) return Array.Empty<BossTimer>();
        var timers = new List<BossTimer>(state.Timers.Count);
        foreach (var timer in state.Timers) timers.Add(!timer.Notified && timer.DueAt <= now ? timer with { Notified = true } : timer);
        Commit(new() { Timers = timers, Durations = state.Durations });
        return due;
    }
    private void Commit(BossTimerState next) { AtomicJson.Write(path, next); state = next; LoadFailed = false; }
}
