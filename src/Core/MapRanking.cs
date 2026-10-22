namespace MapleHelper.Core;

public sealed record RankedMap(Entity Map, int SpawnCount, int TotalSpawns, int TimedSpawns, double? Density, double? SpreadX, double? SpreadY, double? Layout, double? Score);
public static class MapRanking {
    public static List<RankedMap> ForMonster(Catalog catalog, EntityKey monster) {
        if (monster.Kind != EntityKind.Monster) return [];
        var rows = new List<RankedMap>();
        foreach (var map in catalog.Records.Where(r => r.Kind == EntityKind.Map && r.MapData != null)) {
            var data = map.MapData!;
            var repeating = data.Spawns.Where(s => s.RespawnSeconds is null or >= 0).ToList();
            var target = repeating.Where(s => s.MonsterId == monster.Id).ToList();
            if (target.Count == 0) continue;
            double? density = data.Width is > 0 && data.Height is > 0 ? target.Count * 1_000_000d / ((double)data.Width.Value * data.Height.Value) : null;
            double? x = null, y = null, layout = null;
            if (target.All(s => s.X.HasValue && s.Y.HasValue)) {
                x = (double)target.Max(s => s.X!.Value) - target.Min(s => s.X!.Value);
                y = (double)target.Max(s => s.Y!.Value) - target.Min(s => s.Y!.Value);
                // shortcut: coordinate spread approximates travel; use platform pathfinding when class-specific routes are supported.
                layout = 1 / (1 + x / 1200 + y / 300);
            }
            rows.Add(new(map, target.Count, repeating.Count, target.Count(s => s.RespawnSeconds > 0), density, x, y, layout, null));
        }
        var eligible = rows.Where(r => r.Density.HasValue && r.Layout.HasValue).ToList();
        var maxCount = eligible.Count == 0 ? 1 : eligible.Max(r => r.SpawnCount);
        var maxDensity = eligible.Count == 0 ? 1 : eligible.Max(r => r.Density!.Value);
        return rows.Select(r => r.Density.HasValue && r.Layout.HasValue
                ? r with { Score = 45d * r.SpawnCount / maxCount + 35d * r.Density / maxDensity + 20d * r.Layout }
                : r)
            .OrderByDescending(r => r.Score.HasValue).ThenByDescending(r => r.Score)
            .ThenByDescending(r => r.SpawnCount).ThenBy(r => r.Map.Id).ToList();
    }
}
