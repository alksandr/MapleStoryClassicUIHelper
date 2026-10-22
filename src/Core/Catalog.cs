namespace MapleHelper.Core;

public enum EntityKind { Item, Equipment, Monster, Map, Npc, Quest, Skill }
public readonly record struct EntityKey(EntityKind Kind, int Id);
public sealed class Entity {
    public EntityKind Kind { get; set; }
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Image { get; set; }
    public MapData? MapData { get; set; }
    public List<Fact> Facts { get; set; } = [];
    public List<Relation> Relations { get; set; } = [];
    public EntityKey Key => new(Kind, Id);
    public override string ToString() => Name;
}
public sealed record Fact(string Label, string Value);
public sealed record Relation(EntityKey Target, string Label);
public sealed record MonsterSpawn(int MonsterId, int? X, int? Y, int? RespawnSeconds);
public sealed class MapData {
    public int? Width { get; set; }
    public int? Height { get; set; }
    public List<MonsterSpawn> Spawns { get; set; } = [];
}
public sealed class Catalog {
    public int Schema { get; set; } = 1;
    public string Fingerprint { get; set; } = "";
    public DateTimeOffset ImportedAt { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<Entity> Records { get; set; } = [];
    private Dictionary<EntityKey, Entity>? lookup;
    public List<Entity> Search(string query, EntityKind? category = null) {
        query = query.Trim();
        var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Records.Where(r => (!category.HasValue || r.Kind == category) &&
                (r.Id.ToString() == query || tokens.All(t => r.Name.Contains(t, StringComparison.OrdinalIgnoreCase))))
            .OrderBy(r => string.Equals(r.Name, query, StringComparison.OrdinalIgnoreCase) || r.Id.ToString() == query ? 0 : r.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Kind).ThenBy(r => r.Id).Take(200).ToList();
    }
    public Entity? Find(EntityKey key) => (lookup ??= Records.ToDictionary(r => r.Key)).GetValueOrDefault(key);
}
