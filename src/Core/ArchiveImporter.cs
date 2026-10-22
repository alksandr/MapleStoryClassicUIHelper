using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Drawing.Imaging;
using WzComparerR2.WzLib;
namespace MapleHelper.Core;
public sealed class ArchiveImporter {
    public static string Fingerprint(string root) {
        root = Path.GetFullPath(root);
        var data = Path.Combine(root, "Data");
        if (!File.Exists(Path.Combine(root, "MapleStory.exe")) || !Directory.Exists(data)) throw new DirectoryNotFoundException("Choose the MapleStory appdata folder containing MapleStory.exe and Data.");
        var files = Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories).Where(f => new[] { ".wz", ".ms", ".ini", ".md5" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase);
        var text = new StringBuilder("import-v3|").Append(root.ToUpperInvariant());
        foreach (var file in files) { var info = new FileInfo(file); text.Append('|').Append(Path.GetRelativePath(root, file)).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks); }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
    private readonly Dictionary<EntityKey, Entity> records = [];
    private readonly Catalog catalog = new();
    private ArchiveSet archives = null!;
    private string output = "";
    private CancellationToken token;
    private int missingArt;
    private readonly List<(Entity Entity, Wz_Node Node)> descriptions = [];

    public Catalog Import(string root, string output, IProgress<string>? progress, CancellationToken token) {
        this.output = output; this.token = token;
        Directory.CreateDirectory(Path.Combine(output, "images"));
        using var set = new ArchiveSet(root, progress, token);
        archives = set;
        progress?.Report("Reading names and descriptions…");
        Names("Mob.img", EntityKind.Monster); Names("Npc.img", EntityKind.Npc); Names("Map.img", EntityKind.Map);
        Names("Eqp.img", EntityKind.Equipment); Names("Skill.img", EntityKind.Skill);
        foreach (var name in new[] { "Consume.img", "Etc.img", "Ins.img", "Pet.img", "Cash.img" }) Names(name, EntityKind.Item);
        progress?.Report("Reading item and equipment stats…");
        foreach (var image in archives.Images("Character")) WithImage(image, node => {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(image.Text), out var id)) return;
            if (records.TryGetValue(new(EntityKind.Equipment, id), out var entity)) { Facts(entity, node.Nodes["info"]); SaveImage(entity, Node(node, "info/icon")); }
        });
        foreach (var image in archives.Images("Item")) WithImage(image, node => {
            if (int.TryParse(Path.GetFileNameWithoutExtension(image.Text), out var imageId) && records.TryGetValue(new(EntityKind.Item, imageId), out var pet) && Node(node, "info/icon") != null) { Facts(pet, node.Nodes["info"]); SaveImage(pet, Node(node, "info/icon")); }
            foreach (Wz_Node item in node.Nodes) if (int.TryParse(item.Text, out var id) && records.TryGetValue(new(EntityKind.Item, id), out var entity)) { Facts(entity, item.Nodes["info"]); Facts(entity, item.Nodes["spec"]); SaveImage(entity, Node(item, "info/icon")); }
        });
        progress?.Report("Reading monsters and NPCs…");
        foreach (var entity in records.Values.Where(r => r.Kind is EntityKind.Monster or EntityKind.Npc).ToList()) {
            Check();
            var category = entity.Kind == EntityKind.Monster ? "Mob" : "Npc";
            var node = archives.Find($"{category}/{entity.Id:D7}.img");
            if (node == null) continue;
            Facts(entity, node.Nodes["info"]);
            var sprite = Node(node, "stand/0") ?? Node(node, "move/0") ?? Node(node, "fly/0");
            if (sprite == null && int.TryParse(Value(Node(node, "info/link")), out var link)) { var linked = archives.Find($"{category}/{link:D7}.img"); sprite = Node(linked, "stand/0") ?? Node(linked, "move/0"); }
            SaveImage(entity, sprite);
        }
        progress?.Report("Connecting maps, monsters, NPCs, and portals…");
        foreach (var image in archives.Images("Map")) {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(image.Text), out var id) || !records.TryGetValue(new(EntityKind.Map, id), out var map)) continue;
            WithImage(image, node => {
                Facts(map, node.Nodes["info"]);
                SaveImage(map, Node(node, "miniMap/canvas"));
                var left = Number(Node(node, "info/VRLeft")); var right = Number(Node(node, "info/VRRight"));
                var top = Number(Node(node, "info/VRTop")); var bottom = Number(Node(node, "info/VRBottom"));
                map.MapData = new() { Width = Dimension(left, right), Height = Dimension(top, bottom) };
                foreach (var life in Children(node.Nodes["life"])) {
                    if (!int.TryParse(Value(life.Nodes["id"]), out var lifeId)) continue;
                    var kind = Value(life.Nodes["type"]) == "m" ? EntityKind.Monster : EntityKind.Npc;
                    if (kind == EntityKind.Monster) map.MapData.Spawns.Add(new(lifeId, Number(life.Nodes["x"]), Number(life.Nodes["cy"]) ?? Number(life.Nodes["y"]), Number(life.Nodes["mobTime"])));
                    Link(map, new(kind, lifeId), kind == EntityKind.Monster ? "Monsters" : "NPCs", "Locations");
                }
                foreach (var portal in Children(node.Nodes["portal"])) if (int.TryParse(Value(portal.Nodes["tm"]), out var destination) && destination != id) Link(map, new(EntityKind.Map, destination), "Connected maps");
            });
        }
        progress?.Report("Reading quests and rewards…");
        foreach (var image in archives.Images("Quest")) WithImage(image, node => {
            if (!int.TryParse(Path.GetFileNameWithoutExtension(image.Text), out var id)) return;
            var info = node.Nodes["QuestInfo"];
            var name = Value(info?.Nodes["name"]);
            if (name.Length == 0) return;
            var quest = Add(EntityKind.Quest, id, name);
            quest.Description = string.Join("\n\n", Children(info).Where(n => int.TryParse(n.Text, out _)).Select(n => Value(n)));
            foreach (var stage in Children(node.Nodes["Check"])) QuestStage(quest, stage, false);
            foreach (var stage in Children(node.Nodes["Act"])) QuestStage(quest, stage, true);
        });
        progress?.Report("Reading skills and skill levels…");
        foreach (var image in archives.Images("Skill")) WithImage(image, node => {
            foreach (var skill in Children(node.Nodes["skill"])) {
                if (!int.TryParse(skill.Text, out var id) || !records.TryGetValue(new(EntityKind.Skill, id), out var entity)) continue;
                SaveImage(entity, skill.Nodes["icon"]);
                var level = Value(skill.Nodes["masterLevel"]);
                if (level.Length > 0) entity.Facts.Add(new("Master level", level));
                entity.Facts.Add(new("Job ID", (id / 10000).ToString()));
                foreach (var req in Children(skill.Nodes["req"])) if (int.TryParse(req.Text, out var reqId)) Link(entity, new(EntityKind.Skill, reqId), $"Prerequisite level {Value(req)}");
            }
        });
        foreach (var (entity, node) in descriptions) if (entity.Kind == EntityKind.Skill) foreach (var level in Children(node).Where(n => n.Text.StartsWith('h') && int.TryParse(n.Text[1..], out _)).OrderBy(n => int.Parse(n.Text[1..]))) entity.Facts.Add(new($"Level {level.Text[1..]}", Value(level)));
        foreach (var entity in records.Values) {
            entity.Description = GameText.Clean(entity.Description, key => records.GetValueOrDefault(key));
            entity.Facts = entity.Facts.Distinct().Select(f => new Fact(f.Label, GameText.Clean(f.Value, key => records.GetValueOrDefault(key)))).ToList();
            entity.Relations = entity.Relations.Where(l => records.ContainsKey(l.Target)).Distinct().OrderBy(l => l.Label).ThenBy(l => records[l.Target].Name).ToList();
            if (entity.Kind == EntityKind.Quest) entity.Image = entity.Relations.Select(l => records.GetValueOrDefault(l.Target)).FirstOrDefault(r => r?.Kind == EntityKind.Npc && r.Image != null)?.Image;
        }
        catalog.Records = records.Values.OrderBy(r => r.Kind).ThenBy(r => r.Id).ToList();
        if (Enum.GetValues<EntityKind>().Any(k => !catalog.Records.Any(r => r.Kind == k))) throw new InvalidDataException("The game archives did not produce all seven categories. The previous cache is unchanged.");
        if (missingArt > 0) catalog.Warnings.Add($"{missingArt} artwork references could not be decoded; placeholders are used.");
        progress?.Report($"Ready: {catalog.Records.Count:N0} records.");
        return catalog;
    }
    private void Names(string file, EntityKind kind) {
        var root = archives.Find("String/" + file) ?? throw new InvalidDataException($"Missing names: {file}");
        Walk(root);
        void Walk(Wz_Node node) {
            Check();
            if (kind == EntityKind.Equipment && node.Text is "Hair" or "Face") return;
            var name = Value(node.Nodes[kind == EntityKind.Map ? "mapName" : "name"]);
            if (name.Length > 0 && int.TryParse(node.Text, out var id)) {
                var entity = Add(kind, id, name);
                entity.Description = Value(node.Nodes[kind == EntityKind.Map ? "mapDesc" : "desc"]);
                if (kind == EntityKind.Map && node.Nodes["streetName"] != null) entity.Facts.Add(new("Region", Value(node.Nodes["streetName"])));
                descriptions.Add((entity, node));
            } else foreach (Wz_Node child in node.Nodes) Walk(child);
        }
    }
    private Entity Add(EntityKind kind, int id, string name) {
        if (records.TryGetValue(new(kind, id), out var existing)) return existing;
        var entity = new Entity { Kind = kind, Id = id, Name = name };
        records.Add(entity.Key, entity); return entity;
    }
    private static readonly Dictionary<string, string> Labels = new() {
        ["level"] = "Level", ["exp"] = "EXP", ["maxHP"] = "HP", ["maxMP"] = "MP", ["speed"] = "Speed", ["PADamage"] = "Weapon attack", ["MADamage"] = "Magic attack", ["PDDamage"] = "Weapon defense", ["MDDamage"] = "Magic defense", ["acc"] = "Accuracy", ["eva"] = "Avoidability", ["pushed"] = "Knockback", ["boss"] = "Boss", ["undead"] = "Undead", ["elemAttr"] = "Element attributes", ["reqLevel"] = "Required level", ["reqSTR"] = "Required STR", ["reqDEX"] = "Required DEX", ["reqINT"] = "Required INT", ["reqLUK"] = "Required LUK", ["reqJob"] = "Job requirement", ["incSTR"] = "STR", ["incDEX"] = "DEX", ["incINT"] = "INT", ["incLUK"] = "LUK", ["incMHP"] = "HP", ["incMMP"] = "MP", ["incPAD"] = "Weapon attack", ["incMAD"] = "Magic attack", ["incPDD"] = "Weapon defense", ["incMDD"] = "Magic defense", ["incACC"] = "Accuracy", ["incEVA"] = "Avoidability", ["incSpeed"] = "Speed", ["incJump"] = "Jump", ["tuc"] = "Upgrade slots", ["attackSpeed"] = "Attack speed", ["price"] = "Sell price (mesos)", ["hp"] = "HP recovery", ["mp"] = "MP recovery", ["hpR"] = "HP recovery (%)", ["mpR"] = "MP recovery (%)", ["time"] = "Duration (seconds)", ["slotMax"] = "Stack size", ["town"] = "Town", ["mobRate"] = "Spawn multiplier", ["bgm"] = "Music", ["lvmin"] = "Minimum level", ["lvmax"] = "Maximum level", ["money"] = "Mesos", ["pop"] = "Fame", ["count"] = "Quantity", ["prob"] = "Chance (%)", ["incCr"] = "Critical rate", ["incCrd"] = "Critical damage", ["cash"] = "Cash item", ["quest"] = "Quest item"
    };
    private void Facts(Entity entity, Wz_Node? info, string prefix = "") {
        foreach (var property in Children(info)) if (Labels.TryGetValue(property.Text, out var label) && property.Value != null) entity.Facts.Add(new(prefix + label, Value(property)));
    }
    private void QuestStage(Entity quest, Wz_Node stage, bool action) {
        var phase = stage.Text == "0" ? "start" : stage.Text == "1" ? "completion" : $"stage {stage.Text}";
        Facts(quest, stage, action ? $"On {phase}: " : $"{char.ToUpperInvariant(phase[0])}{phase[1..]} requirement: ");
        if (int.TryParse(Value(stage.Nodes["npc"]), out var npc)) Link(quest, new(EntityKind.Npc, npc), phase == "start" ? "Start with" : "Turn in to", "Quests");
        if (int.TryParse(Value(stage.Nodes["nextQuest"]), out var next)) Link(quest, new(EntityKind.Quest, next), "Next quest");
        foreach (var type in new[] { "item", "mob", "quest", "skill" }) foreach (var entry in Children(stage.Nodes[type])) {
            if (!int.TryParse(Value(entry.Nodes["id"]), out var id)) continue;
            var kind = type switch { "mob" => EntityKind.Monster, "quest" => EntityKind.Quest, "skill" => EntityKind.Skill, _ => EntityKind.Item };
            if (kind == EntityKind.Item && records.ContainsKey(new(EntityKind.Equipment, id))) kind = EntityKind.Equipment;
            var count = Value(entry.Nodes["count"]);
            var label = action ? phase == "start" ? "Given on start" : "Reward" : $"{char.ToUpperInvariant(phase[0])}{phase[1..]}: requires";
            if (action && long.TryParse(count, out var quantity) && quantity < 0) { label = $"Consumed on {phase}"; count = Math.Abs(quantity).ToString(CultureInfo.InvariantCulture); }
            Link(quest, new(kind, id), label + (count.Length > 0 ? $" ×{count}" : ""), "Related quests");
        }
    }
    private void Link(Entity from, EntityKey to, string label, string? reverse = null) {
        from.Relations.Add(new(to, label));
        if (reverse != null && records.TryGetValue(to, out var target)) target.Relations.Add(new(from.Key, reverse));
    }
    private void SaveImage(Entity entity, Wz_Node? node) {
        Check();
        if (node == null) return;
        try {
            node = archives.ResolveImage(node);
            if (node?.Value is not Wz_Png png) { missingArt++; return; }
            var relative = $"images/{entity.Kind}-{entity.Id}.png";
            using var bitmap = png.ExtractPng();
            bitmap.Save(Path.Combine(output, relative), ImageFormat.Png);
            entity.Image = relative;
        } catch (Exception ex) when (ex is not OperationCanceledException && ex is not OutOfMemoryException) { missingArt++; if (catalog.Warnings.Count < 10) catalog.Warnings.Add($"Artwork for {entity.Name}: {ex.Message}"); }
    }
    private void WithImage(Wz_Node image, Action<Wz_Node> action) {
        Check();
        var img = (Wz_Image)image.Value;
        if (!img.TryExtract(out var error)) throw new InvalidDataException($"Cannot read {image.FullPathToFile}", error);
        try { action(img.Node); } finally { img.Unextract(); }
    }
    private void Check() => token.ThrowIfCancellationRequested();
    private static Wz_Node? Node(Wz_Node? node, string path) => node?.FindNodeByPath(path.Replace('/', '\\'), true);
    private static IEnumerable<Wz_Node> Children(Wz_Node? node) => node?.Nodes.Cast<Wz_Node>() ?? [];
    private static string Value(Wz_Node? node) => Convert.ToString(node?.Value, CultureInfo.InvariantCulture) ?? "";
    private static int? Number(Wz_Node? node) => int.TryParse(Value(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static int? Dimension(int? start, int? end) => start.HasValue && end.HasValue && (long)end - start is > 0 and <= int.MaxValue ? end - start : null;
}
