using System.Globalization;
using System.Net;
using System.Text.Json;
namespace MapleHelper.Core;
public sealed record CommunityDrop(EntityKey Monster, string MonsterName, EntityKey? Item, string ItemName, decimal? Rate, string Quantity, string Version, string SourcePage);
public sealed class CommunitySnapshot {
    public DateTimeOffset RetrievedAt { get; set; }
    public List<CommunityDrop> Drops { get; set; } = [];
}
public sealed class CommunityDrops(HttpClient client) {
    public async Task<CommunitySnapshot> RefreshAsync(Catalog catalog, string path, CancellationToken token) {
        var monsters = await Rows("monsters", "_pageName=page,id,name_en=name", token);
        var items = await Rows("items", "_pageName=page,id,name_en=name", token);
        var drops = await Rows("monster_drops", "monster_id=monster,item_id=item,drop_rate=rate,min_quantity=min,max_quantity=max,game_version=version", token);
        var monsterKeys = Verify(monsters, catalog, [EntityKind.Monster]);
        var itemKeys = Verify(items, catalog, [EntityKind.Item, EntityKind.Equipment]);
        var result = new CommunitySnapshot { RetrievedAt = DateTimeOffset.UtcNow };
        foreach (var row in drops) {
            token.ThrowIfCancellationRequested();
            var monsterPage = Field(row, "monster");
            var itemPage = Field(row, "item");
            if (!monsterKeys.TryGetValue(monsterPage, out var monster)) continue;
            Entity? item = null;
            if (itemPage is not ("Meso" or "Mesos") && !itemKeys.TryGetValue(itemPage, out item)) continue;
            decimal? rate = decimal.TryParse(Field(row, "rate"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            if (rate < 0 || rate > 100) continue;
            var min = Field(row, "min"); var max = Field(row, "max");
            var quantity = min.Length == 0 ? "Unknown quantity" : max.Length == 0 || min == max ? min : $"{min}–{max}";
            result.Drops.Add(new(monster.Key, monster.Name, item?.Key, item?.Name ?? "Mesos", rate, quantity, Field(row, "version"), monsterPage));
        }
        AtomicJson.Write(path, result, token);
        return result;
    }
    public static CommunitySnapshot? Load(string path) => AtomicJson.Read<CommunitySnapshot>(path);
    private static Dictionary<string, Entity> Verify(List<Dictionary<string, string?>> rows, Catalog catalog, EntityKind[] kinds) {
        var matches = new Dictionary<string, Entity>(StringComparer.Ordinal);
        foreach (var group in rows.GroupBy(r => Field(r, "page"))) {
            if (group.Count() != 1) continue;
            var row = group.First();
            if (!int.TryParse(Field(row, "id"), out var id)) continue;
            var candidates = kinds.Select(k => catalog.Find(new(k, id))).Where(r => r != null && string.Equals(r.Name.Normalize(), Field(row, "name").Normalize(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 1) matches[group.Key] = candidates[0]!;
        }
        return matches;
    }
    private async Task<List<Dictionary<string, string?>>> Rows(string table, string fields, CancellationToken token) {
        var all = new List<Dictionary<string, string?>>();
        for (var offset = 0; offset < 100000; offset += 500) {
            var order = table == "monster_drops" ? "monster_id,item_id,game_version,min_quantity,max_quantity,drop_rate" : "id,_pageName";
            var url = $"https://mapleclassic.wiki/api.php?action=cargoquery&format=json&limit=500&tables={table}&fields={Uri.EscapeDataString(fields)}&order_by={Uri.EscapeDataString(order)}&offset={offset}";
            using var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            try {
                await using var stream = await response.Content.ReadAsStreamAsync(token);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (!json.RootElement.TryGetProperty("cargoquery", out var rows) || rows.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The wiki did not return its expected data. Cached reports are unchanged.");
                foreach (var row in rows.EnumerateArray()) {
                    if (!row.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid wiki row. Cached reports are unchanged.");
                    all.Add(title.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString()));
                }
                if (rows.GetArrayLength() < 500) return all;
            } catch (JsonException ex) { throw new InvalidDataException("The wiki returned unreadable data. Cached reports are unchanged.", ex); }
            await Task.Delay(200, token);
        }
        throw new InvalidDataException("The wiki response exceeded the expected size. Cached reports are unchanged.");
    }
    private static string Field(Dictionary<string, string?> row, string name) => WebUtility.HtmlDecode(row.GetValueOrDefault(name) ?? "").Trim();
}
