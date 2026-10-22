using WzComparerR2.WzLib;
namespace MapleHelper.Core;

public sealed class ArchiveSet : IDisposable {
    private readonly List<Wz_Structure> structures = [];
    private readonly Dictionary<string, List<Wz_Node>> roots = new(StringComparer.OrdinalIgnoreCase);
    public ArchiveSet(string gameRoot, IProgress<string>? progress, CancellationToken token) {
        try {
            foreach (var category in new[] { "String", "Item", "Character", "Mob", "Npc", "Map", "Quest", "Skill" }) {
                token.ThrowIfCancellationRequested();
                progress?.Report($"Opening {category} archives…");
                var wz = new Wz_Structure { AutoDetectExtFiles = true };
                structures.Add(wz);
                Wz_Node? node = null;
                wz.LoadWzFolder(Path.Combine(gameRoot, "Data", category), ref node);
                roots[category] = [node];
            }
            var packs = Path.Combine(gameRoot, "Data", "Packs");
            if (Directory.Exists(packs)) foreach (var file in Directory.EnumerateFiles(packs, "*.ms").Order()) {
                token.ThrowIfCancellationRequested();
                if (!Path.GetFileName(file).StartsWith("Mob_") && !Path.GetFileName(file).StartsWith("Skill_")) continue;
                var wz = new Wz_Structure();
                structures.Add(wz);
                wz.LoadMsFile(file);
                foreach (Wz_Node category in wz.WzNode.Nodes) {
                    if (!roots.ContainsKey(category.Text)) roots[category.Text] = [];
                    roots[category.Text].Insert(0, category);
                }
            }
        } catch { Dispose(); throw; }
    }
    public Wz_Node? Find(string path) {
        var parts = path.Replace('\\', '/').Split('/', 2);
        if (!roots.TryGetValue(parts[0], out var candidates)) return null;
        foreach (var root in candidates) {
            var found = parts.Length == 1 ? root : root.FindNodeByPath(parts[1].Replace('/', '\\'), true);
            if (found != null) return found;
        }
        return null;
    }
    public IEnumerable<Wz_Node> Images(string category) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots[category]) foreach (var image in Walk(root)) {
            var path = image.FullPathToFile;
            if (seen.Add(path)) yield return image;
        }
        static IEnumerable<Wz_Node> Walk(Wz_Node node) {
            foreach (Wz_Node child in node.Nodes) {
                if (child.Text == "_Canvas") continue;
                if (child.Value is Wz_Image && child.Text.EndsWith(".img", StringComparison.OrdinalIgnoreCase)) yield return child;
                else foreach (var image in Walk(child)) yield return image;
            }
        }
    }
    public Wz_Node? ResolveImage(Wz_Node? node) {
        var seen = new HashSet<Wz_Node>();
        for (var depth = 0; node != null && depth < 32 && seen.Add(node); depth++) {
            if (node.Value is Wz_Uol uol) { node = uol.HandleUol(node); continue; }
            var outlink = node.Nodes["_outlink"]?.Value as string;
            if (!string.IsNullOrEmpty(outlink)) { node = Find(outlink); continue; }
            var inlink = node.Nodes["_inlink"]?.Value as string;
            if (!string.IsNullOrEmpty(inlink)) { node = node.GetNodeWzImage()?.Node.FindNodeByPath(inlink.Replace('/', '\\'), true); continue; }
            return node.Value is Wz_Png ? node : null;
        }
        return null;
    }
    public void Dispose() { foreach (var structure in structures) structure.Clear(); structures.Clear(); }
}
