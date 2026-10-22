namespace MapleHelper.Core;
public readonly record struct Hotkey(uint Modifiers, uint Key) {
    public static bool TryParse(string? text, out Hotkey value) {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Any(p => p.Length == 0)) return false;
        uint modifiers = 0;
        foreach (var part in parts[..^1]) {
            uint flag = part.ToUpperInvariant() switch { "CTRL" or "CONTROL" => 2, "SHIFT" => 4, "ALT" => 1, _ => 0 };
            if (flag == 0 || (modifiers & flag) != 0) return false;
            modifiers |= flag;
        }
        var name = parts[^1].ToUpperInvariant();
        uint key = name == "SPACE" ? 32u : name.Length == 1 && char.IsAsciiLetterOrDigit(name[0]) ? (uint)name[0] : 0u;
        if (name.StartsWith('F') && int.TryParse(name[1..], out var function) && function is >= 1 and <= 24 && function != 12) key = (uint)(111 + function);
        if (key == 0) return false;
        value = new(modifiers, key); return true;
    }
}
