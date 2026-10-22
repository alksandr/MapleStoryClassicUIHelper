using System.Text.Json;
namespace MapleHelper.Core;
public sealed record CacheSnapshot(Catalog Catalog, string Directory);
public sealed class CacheStore(string root) {
    public CacheSnapshot? Load() {
        foreach (var manifest in new[] { "current.json", "previous.json" }) {
            try {
                var id = JsonSerializer.Deserialize<string>(File.ReadAllText(Path.Combine(root, manifest)));
                if (!Guid.TryParseExact(id, "N", out _)) continue;
                var dir = Path.Combine(root, "snapshots", id);
                var catalog = JsonSerializer.Deserialize<Catalog>(File.ReadAllText(Path.Combine(dir, "catalog.json")));
                if (catalog?.Schema == 1 && catalog.Records.Count > 0 && catalog.Records.Select(r => r.Key).Distinct().Count() == catalog.Records.Count) return new(catalog, Path.GetFullPath(dir));
            } catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { }
        }
        return null;
    }
    public Task<CacheSnapshot> RebuildAsync(string expected, Func<string, Catalog> build, Func<string> fingerprint, CancellationToken token) => Task.Run(() => {
        var id = Guid.NewGuid().ToString("N");
        var snapshots = Path.GetFullPath(Path.Combine(root, "snapshots"));
        var stage = Path.Combine(snapshots, id + ".tmp");
        var destination = Path.Combine(snapshots, id);
        Directory.CreateDirectory(stage);
        try {
            token.ThrowIfCancellationRequested();
            var catalog = build(stage);
            token.ThrowIfCancellationRequested();
            if (fingerprint() != expected) throw new IOException("Game files changed during import. Wait for the update to finish, then rebuild.");
            if (catalog.Records.Count == 0) throw new InvalidDataException("The import produced no records. The previous cache is unchanged.");
            catalog.Fingerprint = expected;
            catalog.ImportedAt = DateTimeOffset.UtcNow;
            File.WriteAllText(Path.Combine(stage, "catalog.json"), JsonSerializer.Serialize(catalog));
            token.ThrowIfCancellationRequested();
            Directory.Move(stage, destination);
            Directory.CreateDirectory(root);
            var pointer = Path.Combine(root, "current.json");
            var temp = Path.Combine(root, id + ".json.tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(id));
            if (File.Exists(pointer)) File.Replace(temp, pointer, Path.Combine(root, "previous.json"));
            else File.Move(temp, pointer);
            return new CacheSnapshot(catalog, destination);
        } finally {
            // Only this operation's generated staging directory is eligible for removal.
            if (Path.GetDirectoryName(stage) == snapshots && Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }, token);
}
