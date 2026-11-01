using MapleHelper.Core;
using System.Net;
using System.Text;

if (args.Length > 0 && args[0] == "--probe") { ArchiveProbe.Run(args[1], args[2], args.Length > 3 ? args[3] : null); return; }
if (args.Contains("--timer-benchmark")) { BossTimerTests.Benchmark(); return; }
if (args.Length > 0 && args[0] == "--wiki") {
    var cached = new CacheStore(args[1]).Load() ?? throw new Exception("Import a catalog first.");
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleStoryClassicUIHelper/1.0");
    var drops = await new CommunityDrops(http).RefreshAsync(cached.Catalog, Path.Combine(args[1], "community.json"), default);
    Console.WriteLine($"Verified reports: {drops.Drops.Count}, monsters: {drops.Drops.Select(d => d.Monster).Distinct().Count()}");
    Equal(true, drops.Drops.Count > 0);
    return;
}
if (args.Length > 0 && args[0] == "--import") {
    var store = new CacheStore(args.Length > 2 ? args[2] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleStoryClassicUIHelper", "cache"));
    var root = args[1];
    var snapshot = await store.RebuildAsync(ArchiveImporter.Fingerprint(root), dir => new ArchiveImporter().Import(root, dir, new Progress<string>(Console.WriteLine), default), () => ArchiveImporter.Fingerprint(root), default);
    foreach (var g in snapshot.Catalog.Records.GroupBy(r => r.Kind)) Console.WriteLine($"{g.Key}: {g.Count()}, images: {g.Count(r => r.Image != null)}");
    Console.WriteLine($"Snapshot: {snapshot.Directory}");
    foreach (var w in snapshot.Catalog.Warnings) Console.WriteLine($"WARNING {w}");
    Equal("Slime", snapshot.Catalog.Find(new(EntityKind.Monster, 7))?.Name);
    Equal("115", snapshot.Catalog.Find(new(EntityKind.Monster, 7))?.Facts.FirstOrDefault(f => f.Label == "HP")?.Value);
    Equal(true, snapshot.Catalog.Records.All(r => r.Relations.All(l => snapshot.Catalog.Find(l.Target) != null)));
    Equal(true, Enum.GetValues<EntityKind>().All(k => snapshot.Catalog.Records.Any(r => r.Kind == k)));
    Equal(true, snapshot.Catalog.Find(new(EntityKind.Monster, 7))?.Image != null);
    Equal(true, snapshot.Catalog.Find(new(EntityKind.Skill, 1000))?.Facts.Any(f => f.Label == "Level 3" && f.Value.Contains("40")));
    Equal(true, snapshot.Catalog.Find(new(EntityKind.Quest, 1000))?.Relations.Any(r => r.Target == new EntityKey(EntityKind.Npc, 1)));
    var mirror = snapshot.Catalog.Find(new(EntityKind.Quest, 1001))!;
    Equal(true, mirror.Relations.Any(r => r.Target == new EntityKey(EntityKind.Npc, 2) && r.Label == "Start with"));
    Equal(true, mirror.Relations.Any(r => r.Target == new EntityKey(EntityKind.Npc, 1) && r.Label == "Turn in to"));
    Equal(true, mirror.Relations.Any(r => r.Label == "Given on start ×1"));
    Equal(true, mirror.Relations.Any(r => r.Label == "Consumed on completion ×1"));
    Equal(false, mirror.Relations.Any(r => r.Label.Contains("×-")));
    var mapData = snapshot.Catalog.Find(new(EntityKind.Map, 10000020))!.MapData!;
    Equal<int?>(5098, mapData.Width); Equal<int?>(2500, mapData.Height);
    Equal(true, mapData.Spawns.Any(s => s.MonsterId == 7 && s.X == 1886 && s.Y == 95));
    var slimeMaps = MapRanking.ForMonster(snapshot.Catalog, new(EntityKind.Monster, 7));
    Equal(true, slimeMaps.Count > 0 && slimeMaps[0].Score.HasValue);
    Equal(true, slimeMaps.All(r => r.SpawnCount > 0 && r.Score is null or >= 0 and <= 100));
    foreach (var ranked in slimeMaps.Take(3)) Console.WriteLine($"Slime map: {ranked.Map.Name} ({ranked.Map.Id}), score {ranked.Score:0.0}, target placements {ranked.SpawnCount}/{ranked.TotalSpawns}, density {ranked.Density:0.##}, spread {ranked.SpreadX}x{ranked.SpreadY}");
    using (var archive = new ArchiveSet(root, null, default)) {
        var nodes = new WzComparerR2.WzLib.Wz_Node("cycle");
        var a = new WzComparerR2.WzLib.Wz_Node("a") { Value = new WzComparerR2.WzLib.Wz_Uol("b") };
        var b = new WzComparerR2.WzLib.Wz_Node("b") { Value = new WzComparerR2.WzLib.Wz_Uol("a") };
        nodes.Nodes.Add(a); nodes.Nodes.Add(b);
        Equal<WzComparerR2.WzLib.Wz_Node?>(null, archive.ResolveImage(a));
        Equal(true, archive.ResolveImage(archive.Find("Item/Consume/0200.img/02000000/info/icon"))?.Value is WzComparerR2.WzLib.Wz_Png);
    }
    Console.WriteLine("PASS live archive stats, relationships, skill levels, artwork links, and cycle protection");
    return;
}

BossTimerTests.Run();
var tests = new (string Name, Action Run)[] {
    ("Map ranking favors plentiful dense compact spawns and preserves unknown geometry", () => {
        Entity Map(int id, int count, int width, int height, int spread) => new() { Kind = EntityKind.Map, Id = id, Name = "Map " + id, MapData = new() { Width = width, Height = height, Spawns = Enumerable.Range(0, count).Select(i => new MonsterSpawn(7, i * spread, i * spread, null)).ToList() } };
        var best = Map(1, 10, 1000, 500, 10);
        best.MapData!.Spawns.Add(new(9, 0, 0, null));
        best.MapData.Spawns.Add(new(7, 0, 0, -1));
        var sparse = Map(2, 5, 4000, 2000, 300);
        var unknown = Map(3, 3, 0, 0, 0);
        var catalog = new Catalog { Records = [sparse, unknown, best, new() { Kind = EntityKind.Map, Id = 4, Name = "No target", MapData = new() }] };
        var ranked = MapRanking.ForMonster(catalog, new(EntityKind.Monster, 7));
        Equal(3, ranked.Count); Equal(1, ranked[0].Map.Id); Equal(10, ranked[0].SpawnCount);
        Equal(11, ranked[0].TotalSpawns); Equal<double?>(20, ranked[0].Density);
        Equal(true, ranked[0].Score > ranked[1].Score);
        Equal<double?>(null, ranked[2].Score);
        Equal(0, MapRanking.ForMonster(catalog, new(EntityKind.Item, 7)).Count);
        sparse.MapData!.Spawns[0] = new(7, null, 0, 30);
        Equal<double?>(null, MapRanking.ForMonster(catalog, new(EntityKind.Monster, 7)).Single(r => r.Map.Id == 2).Score);
        Equal(1, MapRanking.ForMonster(catalog, new(EntityKind.Monster, 7)).Single(r => r.Map.Id == 2).TimedSpawns);
        var compact = Map(10, 5, 1000, 1000, 10);
        var spreadOut = Map(11, 5, 1000, 1000, 100);
        var large = Map(12, 5, 2000, 2000, 10);
        var comparison = MapRanking.ForMonster(new() { Records = [spreadOut, large, compact] }, new(EntityKind.Monster, 7));
        Equal(10, comparison[0].Map.Id);
        Equal(true, comparison.Single(r => r.Map.Id == 10).Score > comparison.Single(r => r.Map.Id == 11).Score);
        Equal(true, comparison.Single(r => r.Map.Id == 10).Score > comparison.Single(r => r.Map.Id == 12).Score);
    }),
    ("Exact name precedes prefix and substring", () => {
        var index = new Catalog { Records = [new() { Kind = EntityKind.Item, Id = 1, Name = "Slime Bubble" }, new() { Kind = EntityKind.Monster, Id = 1, Name = "Slime" }, new() { Kind = EntityKind.Monster, Id = 2, Name = "King Slime" }] };
        Equal("Slime|Slime Bubble|King Slime", string.Join('|', index.Search("slime").Select(r => r.Name)));
        Equal(2, index.Search("slime", EntityKind.Monster).Count);
        Equal(2, index.Search("1").Count);
        Equal("Slime Bubble", index.Find(new(EntityKind.Item, 1))!.Name);
    }),
    ("Duplicate names and category IDs remain distinct", () => {
        var index = new Catalog { Records = [new() { Kind = EntityKind.Monster, Id = 1, Name = "Slime" }, new() { Kind = EntityKind.Monster, Id = 2, Name = "Slime" }, new() { Kind = EntityKind.Item, Id = 1, Name = "Slime" }] };
        Equal(3, index.Search(" SlImE ").Count);
        Equal(0, index.Search("not here").Count);
    }),
    ("Game text resolves references without leaving control codes", () => Equal("Talk to Sera.\nBring Slime Bubble.", GameText.Clean("Talk to #b#p1##k.\\nBring #t4000004#.", key => new() { Kind = key.Kind, Id = key.Id, Name = key.Kind == EntityKind.Npc ? "Sera" : "Slime Bubble" }))),
    ("Cancelled and changed-source rebuilds preserve the previous cache", () => {
        var path = Path.Combine(Path.GetTempPath(), "maple-helper-test-" + Guid.NewGuid().ToString("N"));
        try {
            var cache = new CacheStore(path);
            cache.RebuildAsync("one", _ => new Catalog { Records = [new() { Name = "Original" }] }, () => "one", default).GetAwaiter().GetResult();
            Equal(true, cache.Load() != null);
            using var cts = new CancellationTokenSource();
            try { cache.RebuildAsync("two", _ => { cts.Cancel(); return new(); }, () => "two", cts.Token).GetAwaiter().GetResult(); throw new Exception("Expected cancellation"); } catch (OperationCanceledException) { }
            Equal("Original", cache.Load()!.Catalog.Records[0].Name);
            try { cache.RebuildAsync("two", _ => new(), () => "changed", default).GetAwaiter().GetResult(); throw new Exception("Expected source change rejection"); } catch (IOException) { }
            Equal("Original", cache.Load()!.Catalog.Records[0].Name);
            File.WriteAllText(Path.Combine(path, "current.json.tmp"), "interrupted");
            Equal("Original", new CacheStore(path).Load()!.Catalog.Records[0].Name);
        } finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }),
    ("Community drops require verified identity and preserve unknown rates", () => {
        var catalog = new Catalog { Records = [new() { Kind = EntityKind.Monster, Id = 7, Name = "Slime" }, new() { Kind = EntityKind.Item, Id = 4000004, Name = "Slime Bubble" }] };
        using var http = new HttpClient(new WikiHandler(false));
        var path = Path.Combine(Path.GetTempPath(), "maple-drops-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var data = new CommunityDrops(http).RefreshAsync(catalog, path, default).GetAwaiter().GetResult();
            Equal(1, data.Drops.Count);
            Equal<decimal?>(null, data.Drops[0].Rate);
            Equal("Slime Bubble", data.Drops[0].ItemName);
            using var broken = new HttpClient(new WikiHandler(true));
            try { new CommunityDrops(broken).RefreshAsync(catalog, path, default).GetAwaiter().GetResult(); throw new Exception("Expected invalid response rejection"); } catch (InvalidDataException) { }
            Equal(1, CommunityDrops.Load(path)!.Drops.Count);
        } finally { if (File.Exists(path)) File.Delete(path); }
    }),
    ("Hotkeys reject missing modifiers and reserved keys", () => {
        Equal(true, Hotkey.TryParse("Ctrl+Shift+Space", out var hotkey));
        Equal(6u, hotkey.Modifiers);
        Equal(32u, hotkey.Key);
        foreach (var invalid in new[] { "Space", "Ctrl+F12", "Ctrl++M", "Ctrl+Ctrl+M", "Win+M", "Ctrl+NoSuchKey" }) Equal(false, Hotkey.TryParse(invalid, out _));
    }),
    ("Damaged archives do not replace a working cache or leave locked files", () => {
        var folder = Path.Combine(Path.GetTempPath(), "maple-bad-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "game", "Data", "String"));
        try {
            var cache = new CacheStore(Path.Combine(folder, "cache"));
            cache.RebuildAsync("valid", _ => new Catalog { Records = [new() { Name = "Preserved" }] }, () => "valid", default).GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(folder, "game", "MapleStory.exe"), "");
            File.WriteAllText(Path.Combine(folder, "game", "Data", "String", "String.wz"), "broken archive");
            var root = Path.Combine(folder, "game");
            var rejected = false;
            try { cache.RebuildAsync(ArchiveImporter.Fingerprint(root), dir => new ArchiveImporter().Import(root, dir, null, default), () => ArchiveImporter.Fingerprint(root), default).GetAwaiter().GetResult(); }
            catch (Exception) { rejected = true; }
            Equal(true, rejected); Equal("Preserved", cache.Load()!.Catalog.Records[0].Name);
        } finally { Directory.Delete(folder, true); }
    })
};
var failed = 0;
foreach (var test in tests) {
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Environment.ExitCode = failed == 0 ? 0 : 1;
static void Equal<T>(T expected, T actual) { if (!Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }

sealed class WikiHandler(bool broken) : HttpMessageHandler {
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
        var query = request.RequestUri!.Query;
        if (query.Contains("._ID")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"error\":{\"code\":\"db_error\"}}") });
        var rows = query.Contains("tables=monsters") ? """{"cargoquery":[{"title":{"id":"7","name":"Slime","page":"Slime"}},{"title":{"id":"8","name":"Other Slime","page":"Other"}}]}""" :
            query.Contains("tables=items") ? """{"cargoquery":[{"title":{"id":"4000004","name":"Slime Bubble","page":"Slime Bubble"}},{"title":{"id":"4000004","name":"Wrong name","page":"Wrong"}}]}""" :
            """{"cargoquery":[{"title":{"monster":"Slime","item":"Slime Bubble","rate":null,"version":"0.9.0","min":"1","max":"1"}},{"title":{"monster":"Other","item":"Slime Bubble","rate":"25"}},{"title":{"monster":"Slime","item":"Wrong","rate":"10"}}]}""";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(broken ? "{\"error\":\"busy\"}" : rows, Encoding.UTF8, "application/json") });
    }
}
