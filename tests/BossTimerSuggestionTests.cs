using MapleHelper.Core;

internal static class BossTimerSuggestionTests {
    public static void Run() {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Entity Map(int id, int boss, params int?[] seconds) => new() { Kind = EntityKind.Map, Id = id, MapData = new() { Spawns = seconds.Select(s => new MonsterSpawn(boss, null, null, s)).ToList() } };
        var mushmom = Map(10001005, 700000, 3600);
        var current = new BossTimer(Guid.NewGuid(), 700000, "Mushmom", mushmom.Id, "Map", 1, 90, DateTimeOffset.UtcNow);
        var suggestion = BossTimerSuggestions.Resolve(700000, mushmom, 120, current)!;
        Check(suggestion.Seconds == 90 && suggestion.SourceLabel == "Current timer", "Editing must preserve current duration");
        suggestion = BossTimerSuggestions.Resolve(700000, mushmom, 120)!;
        Check(suggestion.Seconds == 120 && suggestion.SourceLabel == "Your saved duration", "Remembered duration must beat client/community");
        suggestion = BossTimerSuggestions.Resolve(700000, mushmom)!;
        Check(suggestion.Seconds == 3600 && suggestion.SourceLabel == "Client estimate", "Positive client timing must beat fallback");
        var ordinary = Map(50, 99, 180, 180, 0, null, -1);
        Check(BossTimerSuggestions.Resolve(99, ordinary)?.Seconds == 180, "Ignore invalid values when positive placements agree");
        ordinary.MapData!.Spawns.Add(new(98, null, null, 999));
        Check(BossTimerSuggestions.Resolve(99, ordinary)?.Seconds == 180, "Ignore other monsters' placements");
        ordinary.MapData.Spawns.Add(new(99, null, null, 240));
        Check(BossTimerSuggestions.Resolve(99, ordinary) == null, "Conflicting unknown client timings must remain unknown");
        mushmom.MapData!.Spawns.Add(new(700000, null, null, 7200));
        suggestion = BossTimerSuggestions.Resolve(700000, mushmom)!;
        Check(suggestion.SourceLabel == "Community estimate" && suggestion.Seconds == 3600, "Conflict must use earliest community fallback");
        Check(suggestion.Caveat!.Contains("60–120") && suggestion.SourceUrl == "https://meowdb.com/msclassic/bosses" && suggestion.VerifiedOn == new DateOnly(2026, 10, 31), "Community data must expose range and provenance");
        foreach (var (boss, map, seconds) in new[] { (700004, 10000013, 3600), (700007, 10004057, 3600), (700000, 10001005, 3600), (700006, 10003112, 3600), (700002, 10005078, 3600), (700003, 10006121, 3600), (700001, 10005105, 10800) }) {
            Check(BossTimerSuggestions.Resolve(boss, Map(map, boss, 0, -1, null))?.Seconds == seconds, "Missing/invalid client values must use exact fallback");
            Check(BossTimerSuggestions.Resolve(boss, Map(map + 1, boss)) == null, "Fallback must not leak to another map");
            Check(BossTimerSuggestions.Resolve(boss + 100, Map(map, boss)) == null, "Fallback must not leak to another boss");
        }
        Check(BossTimerSuggestions.Resolve(99, Map(50, 99), editing: current) == null, "Unrelated edited timer must not supply a duration");
        Check(BossTimerSuggestions.Resolve(99, Map(50, 99), rememberedSeconds: 0) == null, "Zero saved duration must not become a suggestion");
        Console.WriteLine("PASS timer suggestions: precedence, matching, client conflicts/invalid values, earliest fallback and provenance");
    }
}
