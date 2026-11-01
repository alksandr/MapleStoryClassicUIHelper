using MapleHelper.Core;

internal static class BossTimerTests {
    public static void Benchmark() {
        var path = Path.Combine(Path.GetTempPath(), "maple-timer-bench-" + Guid.NewGuid().ToString("N"), "timers.json");
        var now = DateTimeOffset.UtcNow;
        try {
            foreach (var count in new[] { 1000, 10000 }) {
                var state = new BossTimerState { Timers = Enumerable.Range(1, count).Select(i => new BossTimer(Guid.NewGuid(), i, "Boss", 10, "Map", 1, 60, now.AddSeconds(60))).ToList() };
                AtomicJson.Write(path, state);
                var store = new BossTimers(path);
                for (var i = 0; i < 2000; i++) store.TakeDue(now);
                var watch = new System.Diagnostics.Stopwatch();
                var bytes = GC.GetAllocatedBytesForCurrentThread(); var gc = GC.CollectionCount(0);
                watch.Start();
                for (var i = 0; i < 10000; i++) store.TakeDue(now);
                watch.Stop();
                var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
                Console.WriteLine($"Timer scan N={count}: {watch.Elapsed.TotalMilliseconds / 10000:0.0000} ms/tick, {allocated / 10000} B/tick, Gen0={GC.CollectionCount(0) - gc}");
                // Dense expiry includes the durable receipt write, not just the scan.
                bytes = GC.GetAllocatedBytesForCurrentThread(); gc = GC.CollectionCount(0); watch.Restart();
                var due = store.TakeDue(now.AddMinutes(2)); watch.Stop();
                if (due.Count != count) throw new Exception("Dense expiry missed a timer");
                Console.WriteLine($"Timer dense expiry N={count}: {watch.Elapsed.TotalMilliseconds:0.000} ms including disk, {GC.GetAllocatedBytesForCurrentThread() - bytes} B, Gen0={GC.CollectionCount(0) - gc}");
            }
        } finally { Directory.Delete(Path.GetDirectoryName(path)!, true); }
    }
    public static void Run() {
        var root = Path.Combine(Path.GetTempPath(), "maple-timers-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "boss-timers.json");
        var now = DateTimeOffset.Parse("2026-10-31T12:00:00Z");
        void Check(bool value) { if (!value) throw new Exception("Boss timer assertion failed"); }
        try {
            var store = new BossTimers(path);
            Check(store.Timers.Count == 0 && !store.LoadFailed);
            Check(store.NextChannel(1, 10) == 1);
            var one = store.Start(1, "Boss", 10, "Map", 1, 60, now);
            store.Start(1, "Boss", 10, "Map", 2, 60, now);
            Check(store.NextChannel(1, 10) == 3 && store.NextChannel(2, 10) == 1 && store.NextChannel(1, 11) == 1);
            Check(store.Timers.Count == 2 && store.RememberedSeconds(1, 10) == 60);
            Check(store.TakeDue(now.AddSeconds(59)).Count == 0);
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) {
                try { store.TakeDue(now.AddSeconds(60)); throw new Exception("Expected notification receipt write failure"); }
                catch (IOException) { }
                Check(store.Timers.All(t => !t.Notified));
            }
            Check(store.TakeDue(now.AddSeconds(60)).Count == 2);
            Check(store.TakeDue(now.AddHours(5)).Count == 0);
            var loaded = new BossTimers(path);
            Check(loaded.Timers.Count == 2 && loaded.TakeDue(now.AddDays(1)).Count == 0);
            Check(loaded.Timers[0].BossName == "Boss" && loaded.Timers[0].MapName == "Map");
            var restarted = loaded.Start(1, "Boss", 10, "Map", 1, 120, now.AddHours(1));
            Check(loaded.Timers.Count == 2 && restarted.DueAt == now.AddHours(1).AddSeconds(120));
            Check(loaded.TakeDue(now.AddMinutes(59)).Count == 0);
            var catchup = new BossTimers(path);
            Check(catchup.TakeDue(now.AddDays(2)).Count == 1);
            Check(new BossTimers(path).TakeDue(now.AddDays(2)).Count == 0);
            // Editing into an existing channel merges the two identities.
            var edited = catchup.Start(1, "Boss", 10, "Map", 2, 30, now, restarted.Id);
            Check(catchup.Timers.Count == 1 && catchup.Timers[0].Id == edited.Id);
            Check(catchup.NextChannel(1, 10) == 1); // Fill an unused lower channel before extending the set.
            catchup.Remove(edited.Id);
            Check(catchup.NextChannel(1, 10) == 1);
            Check(new BossTimers(path).Timers.Count == 0);
            Check(new BossTimers(path).RememberedSeconds(1, 10) == 30);
            foreach (var invalid in new[] { 0, -1 }) {
                try { store.Start(1, "Boss", 10, "Map", 1, invalid, now); throw new Exception("Accepted bad duration"); }
                catch (ArgumentOutOfRangeException) { }
                try { store.Start(1, "Boss", 10, "Map", invalid, 60, now); throw new Exception("Accepted bad channel"); }
                catch (ArgumentOutOfRangeException) { }
            }
            File.WriteAllText(path, "{broken");
            Check(new BossTimers(path).LoadFailed);
            Check(File.ReadAllText(path) == "{broken");
            // A failed write must leave the in-memory state unchanged.
            var blocked = Path.Combine(root, "directory"); Directory.CreateDirectory(blocked);
            var failure = new BossTimers(blocked);
            try { failure.Start(1, "Boss", 10, "Map", 1, 60, now); throw new Exception("Expected write failure"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Check(failure.Timers.Count == 0);
            Check(new Settings().BossTimerSound);
            Console.WriteLine("PASS boss timers: channels, restart/edit/remove, deadlines, catch-up, persistence, validation and failed writes");
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
