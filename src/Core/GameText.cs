using System.Text.RegularExpressions;
namespace MapleHelper.Core;
public static class GameText {
    public static string Clean(string? text, Func<EntityKey, Entity?>? find = null) {
        text = (text ?? "").Replace("\\r\\n", "\n").Replace("\\n", "\n").Replace("\\r", "");
        text = Regex.Replace(text, @"#([ptzimsoq])(\d+)#", m => {
            var kind = m.Groups[1].Value switch { "p" => EntityKind.Npc, "m" => EntityKind.Map, "o" => EntityKind.Monster, "s" => EntityKind.Skill, "q" => EntityKind.Quest, _ => EntityKind.Item };
            var id = int.Parse(m.Groups[2].Value);
            return find?.Invoke(new(kind, id))?.Name ?? (kind == EntityKind.Item ? find?.Invoke(new(EntityKind.Equipment, id))?.Name : null) ?? $"{kind} {id}";
        });
        text = Regex.Replace(text, @"#f[^#]*#", "");
        text = Regex.Replace(text, @"#[bkrgdenlc]|#h\s*#", "");
        return text.Replace("#", "").Trim();
    }
}
