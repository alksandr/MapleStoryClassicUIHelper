using System.Globalization;

namespace MapleHelper.Core;
public static class BossTimerChannels {
    // A per-submission resource guard, independent of valid channel IDs or world capacity.
    public const int MaxBatchSize = 1000;
    public static bool TryParse(string text, out IReadOnlyList<int> channels, out string error) {
        channels = Array.Empty<int>();
        error = "Enter positive channels such as 1–10 or 1, 3, 5–8. Ranges must be ascending.";
        var selected = new HashSet<int>();
        foreach (var token in text.Replace('–', '-').Replace('—', '-').Split(',')) {
            var range = token.Split('-');
            if (range.Length is < 1 or > 2 || !Number(range[0], out var first)) return false;
            var last = first;
            if (range.Length == 2 && (!Number(range[1], out last) || last < first)) return false;
            if ((long)last - first + 1 > MaxBatchSize) { error = LimitMessage; return false; }
            for (long value = first; value <= last; value++) {
                selected.Add((int)value);
                if (selected.Count > MaxBatchSize) { error = LimitMessage; return false; }
            }
        }
        channels = selected.Order().ToArray(); error = ""; return true;
    }
    private static bool Number(string text, out int value) => int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    public static string LimitMessage => $"Select up to {MaxBatchSize:N0} channels at a time.";
}
