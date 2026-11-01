namespace MapleHelper.Core;

public sealed record BossTimerSuggestion(int Seconds, string SourceLabel, string? SourceUrl = null,
    string? Caveat = null, DateOnly? VerifiedOn = null);

public static class BossTimerSuggestions {
    // Classic World field-boss map IDs, verified against the source on 2026-10-31.
    // Entries supply durations only; they never add records or locations to the catalog.
    private const string CommunitySource = "https://meowdb.com/msclassic/bosses";
    private static readonly DateOnly CommunityVerifiedOn = new(2026, 10, 31);
    private static readonly Dictionary<(int Boss, int Map), (int Earliest, int? Latest)> Community = new() {
        [(700004, 10000013)] = (3600, 7200), // Mano
        [(700007, 10004057)] = (3600, 7200), // Stumpy
        [(700000, 10001005)] = (3600, 7200), // Mushmom
        [(700006, 10003112)] = (3600, 7200), // Shade
        [(700002, 10005078)] = (3600, 7200), // Zombie Mushmom
        [(700003, 10006121)] = (3600, 7200), // Rotten Mushmom (requires a linked map in the installed client)
        [(700001, 10005105)] = (10800, null) // Jr. Balrog
    };

    // Called only when opening/selecting a map/submitting a dialog, never per tick.
    // O(S) for S placements on the selected map, including conflicting/dense cases.
    public static BossTimerSuggestion? Resolve(int bossId, Entity map, int? rememberedSeconds = null, BossTimer? editing = null) {
        if (map.Kind != EntityKind.Map) return null;
        if (editing is { DurationSeconds: > 0 } && editing.BossId == bossId && editing.MapId == map.Id)
            return new(editing.DurationSeconds, "Current timer");
        if (rememberedSeconds is > 0) return new(rememberedSeconds.Value, "Your saved duration");
        var positive = map.MapData?.Spawns.Where(s => s.MonsterId == bossId && s.RespawnSeconds > 0)
            .Select(s => s.RespawnSeconds!.Value).Distinct().Take(2).ToArray();
        if (positive is { Length: 1 }) return new(positive[0], "Client estimate", Caveat: "Client timing; the server may respawn the boss later.");
        if (!Community.TryGetValue((bossId, map.Id), out var fallback)) return null;
        var caveat = fallback.Latest.HasValue
            ? $"Reported range: {fallback.Earliest / 60}–{fallback.Latest / 60} minutes. Uses the earliest time; the boss may appear later."
            : "Community estimate; actual server respawn timing may differ.";
        return new(fallback.Earliest, "Community estimate", CommunitySource, caveat, CommunityVerifiedOn);
    }
}
