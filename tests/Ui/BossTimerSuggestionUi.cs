using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Threading;
using MapleHelper;
using MapleHelper.Core;

internal static class BossTimerSuggestionUi {
    public static void Run(Window owner, string root) {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var path = Path.Combine(root, "suggestions.json");
        var store = new BossTimers(path);
        var client = new Entity { Kind = EntityKind.Map, Id = 10001005, Name = "Client map", MapData = new() { Spawns = [new(700000, null, null, 3600)] } };
        var community = new Entity { Kind = EntityKind.Map, Id = 10005078, Name = "Community map" };
        var unknown = new Entity { Kind = EntityKind.Map, Id = 99, Name = "Unknown map" };
        TextBox Box(BossTimerDialog dialog, string name) => (TextBox)dialog.FindName(name + "Box");
        void Clear(BossTimerDialog dialog) { Box(dialog, "Hours").Clear(); Box(dialog, "Minutes").Clear(); Box(dialog, "Seconds").Clear(); }
        void Start(BossTimerDialog dialog) => ((Button)dialog.FindName("SaveTimerButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        bool? Show(BossTimerDialog dialog, Action<BossTimerDialog> action) {
            Exception? failure = null; dialog.Owner = owner;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => {
                try { action(dialog); } catch (Exception ex) { failure = ex; dialog.Close(); }
            }));
            var result = dialog.ShowDialog();
            if (failure != null) throw failure;
            return result;
        }
        var draft = new BossTimerDialog("Mushmom", [client, unknown], store, 700000);
        Show(draft, d => {
            Check(Box(d, "Hours").Text == "1" && Box(d, "Minutes").Text == "0" && Box(d, "Seconds").Text == "0", "First-use client estimate fills complete duration");
            Check(((TextBlock)d.FindName("SuggestionText")).Text.Contains("Client estimate"), "Client estimate is labeled");
            Check(!File.Exists(path), "Opening and prefilling do not write timer storage");
            Box(d, "Minutes").Text = "15";
            Check(Box(d, "Hours").Text == "1", "Typing does not replace the other entered duration components");
            var maps = (ComboBox)d.FindName("MapChoice"); maps.SelectedIndex = 1;
            Check(Box(d, "Hours").Text == "" && Box(d, "Minutes").Text == "", "Unknown map starts blank");
            Box(d, "Hours").Text = "2";
            maps.SelectedIndex = 0;
            Check(Box(d, "Minutes").Text == "15", "Switching maps restores a manual draft");
            maps.SelectedIndex = 1;
            Check(Box(d, "Hours").Text == "2" && Box(d, "Minutes").Text == "", "Partial drafts remain intact and separate by map");
            d.DialogResult = false;
        });
        Check(!File.Exists(path), "Cancelling discards drafts without saving");
        var fallback = new BossTimerDialog("Zombie Mushmom", [community], store, 700002);
        Check(Show(fallback, d => {
            Check(Box(d, "Hours").Text == "1", "Missing client timing prefills community fallback");
            Check(((TextBlock)d.FindName("SuggestionText")).Text.Contains("Community estimate") && ((TextBlock)d.FindName("SuggestionCaveat")).Text.Contains("60–120"), "Community source and earliest-time caveat render");
            Check(((TextBlock)d.FindName("SuggestionSource")).Inlines.OfType<Hyperlink>().Single().NavigateUri.AbsoluteUri == "https://meowdb.com/msclassic/bosses", "Community source is a clickable link");
            Clear(d); Box(d, "Minutes").Text = "5";
            Start(d);
        }) == true && fallback.DurationSeconds == 300, "Partial manual duration is not combined with suggested hours");
        store.Start(700002, "Zombie Mushmom", community.Id, community.Name, fallback.Channel, fallback.DurationSeconds, DateTimeOffset.UtcNow);
        var reloaded = new BossTimers(path);
        var saved = new BossTimerDialog("Zombie Mushmom", [community], reloaded, 700002);
        Show(saved, d => {
            Check(Box(d, "Hours").Text == "0" && Box(d, "Minutes").Text == "5" && ((TextBlock)d.FindName("SuggestionText")).Text.Contains("Your saved duration"), "Started manual override is remembered across store restart");
            d.DialogResult = false;
        });
        var fresh = new BossTimerDialog("Mushmom", [client], store, 700000);
        Check(Show(fresh, d => { Clear(d); Start(d); }) == true && fresh.DurationSeconds == 3600, "Starting with all fields blank reapplies the complete suggestion");
        store.Start(700000, "Mushmom", client.Id, client.Name, fresh.Channel, fresh.DurationSeconds, DateTimeOffset.UtcNow);
        Check(new BossTimers(path).RememberedSeconds(700000, client.Id) == 3600, "Starting an accepted suggestion persists its duration");
    }
}
