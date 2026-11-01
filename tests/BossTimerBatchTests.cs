using MapleHelper.Core;

internal static class BossTimerBatchTests {
    public static void Run() {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        Check(BossTimerChannels.TryParse("1–10, 3, 12—13, 15-16", out var parsed, out _) && string.Join(',', parsed) == "1,2,3,4,5,6,7,8,9,10,12,13,15,16", "Ranges, Unicode dashes, lists and duplicates must work");
        Check(BossTimerChannels.TryParse(" 7 , 2 - 4, 4 ", out parsed, out _) && string.Join(',', parsed) == "2,3,4,7", "Selection must be trimmed, sorted and unique");
        Check(BossTimerChannels.TryParse(int.MaxValue.ToString(), out parsed, out _) && parsed.Single() == int.MaxValue, "Channel ID is independent of batch-size limit");
        foreach (var input in new[] { "", "0", "-1", "10-1", "1,", "1,,2", "1-x", "1-2-3", "1.5", "2147483648", "1-2147483647", $"1-{BossTimerChannels.MaxBatchSize + 1}" })
            Check(!BossTimerChannels.TryParse(input, out parsed, out var error) && parsed.Count == 0 && !string.IsNullOrWhiteSpace(error), "Invalid or excessive range must be rejected completely: " + input);
        Check(BossTimerChannels.TryParse($"1-{BossTimerChannels.MaxBatchSize}", out parsed, out _) && parsed.Count == BossTimerChannels.MaxBatchSize, "Maximum batch must be accepted");
        var root = Path.Combine(Path.GetTempPath(), "maple-batch-" + Guid.NewGuid().ToString("N")); var path = Path.Combine(root, "timers.json");
        try {
            var store = new BossTimers(path); var now = DateTimeOffset.Parse("2026-10-31T12:00:00Z");
            var existing = store.Start(1, "Boss", 10, "Map", 2, 60, now);
            var untouched = store.Start(1, "Boss", 10, "Map", 20, 60, now);
            var other = store.Start(2, "Other", 10, "Map", 2, 60, now);
            store.TakeDue(now.AddMinutes(1));
            var batch = store.StartMany(1, "Boss", 10, "Map", [1, 2, 3, 3], 120, now.AddHours(1));
            Check(batch.Count == 3 && store.Timers.Count == 5, "Batch must deduplicate channels and preserve unrelated timers");
            Check(batch.Single(t => t.Channel == 2).Id == existing.Id && batch.All(t => !t.Notified), "Selected existing timers restart with their identity and reset notification receipt");
            Check(batch.All(t => t.DueAt == now.AddHours(1).AddSeconds(120)), "Every channel starts at the same instant");
            Check(store.Timers.Single(t => t.Id == untouched.Id).DueAt == untouched.DueAt && store.Timers.Single(t => t.Id == other.Id).DueAt == other.DueAt, "Unselected timers keep their deadlines");
            Check(new BossTimers(path).Timers.Count == 5 && new BossTimers(path).RememberedSeconds(1, 10) == 120, "Batch and chosen duration must persist together");
            var original = File.ReadAllText(path);
            foreach (var bad in new int[][] { [], [1, 0, 3], [-1], Enumerable.Range(1, BossTimerChannels.MaxBatchSize + 1).ToArray() }) {
                try { store.StartMany(1, "Boss", 10, "Map", bad, 90, now); throw new Exception("Invalid batch accepted"); }
                catch (ArgumentException) { }
                Check(File.ReadAllText(path) == original, "Invalid batch must not partially save");
            }
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                try { store.StartMany(1, "Boss", 10, "Map", [4, 5, 6], 90, now); throw new Exception("Expected write failure"); }
                catch (IOException) { }
                Check(store.Timers.Count == 5 && store.RememberedSeconds(1, 10) == 120, "Failed batch write must preserve all state");
            }
            Check(File.ReadAllText(path) == original, "Failed batch write must preserve the previous file");
            Check(store.StartMany(1, "Boss", 10, "Map", Enumerable.Range(1, BossTimerChannels.MaxBatchSize).ToArray(), 60, now).Count == BossTimerChannels.MaxBatchSize, "Store must accept the maximum batch size");
            Console.WriteLine("PASS timer batches: ranges/lists, validation, deduplication, common deadlines, restart identity and atomic persistence");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
