using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MapleHelper.Core;

namespace MapleHelper;
public partial class MainWindow {
    private BossTimers bossTimers = null!;
    private readonly DispatcherTimer bossTimerTick = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly List<(BossTimer Timer, TextBlock Countdown)> countdowns = new();
    private bool showingTimers, timerSaveError;
    private void InitializeBossTimers() {
        bossTimers = new BossTimers(Path.Combine(App.DataRoot, "boss-timers.json"));
        if (bossTimers.LoadFailed) StatusText.Text = "Saved boss timers could not be read. The original file is preserved until you save a timer.";
        bossTimerTick.Tick += (_, _) => CheckBossTimers();
        IsVisibleChanged += (_, _) => { if (IsVisible && showingTimers) UpdateCountdowns(DateTimeOffset.UtcNow); };
        Loaded += (_, _) => CheckBossTimers();
        bossTimerTick.Start();
    }
    private void ToggleTimersClick(object sender, RoutedEventArgs e) => SetTimerView(!showingTimers);
    private void SetTimerView(bool show) {
        showingTimers = show;
        TimerView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        LookupSearch.Visibility = Categories.Visibility = LookupBody.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        TimersButton.Content = show ? "Field guide" : "Boss timers";
        if (show) { DrawBossTimers(); TimersButton.Focus(); } else SearchBox.Focus();
    }
    private void DrawAddTimer(Entity boss) {
        var maps = boss.Relations.Where(r => r.Target.Kind == EntityKind.Map).Select(r => snapshot?.Catalog.Find(r.Target)).OfType<Entity>().DistinctBy(m => m.Id).OrderBy(m => m.Name).ToList();
        var add = new Button { Content = "Add timer", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = maps.Count > 0, Margin = new Thickness(0, 0, 0, 10), ToolTip = maps.Count > 0 ? "Start a manual respawn countdown for a map and channel" : "No linked map is available in the client data" };
        add.Click += (_, _) => {
            var dialog = new BossTimerDialog(boss.Name, maps, bossTimers, boss.Id) { Owner = this };
            if (dialog.ShowDialog() == true) TimerChange(() => bossTimers.Start(boss.Id, boss.Name, dialog.Map.Id, dialog.Map.Name, dialog.Channel, dialog.DurationSeconds, DateTimeOffset.UtcNow));
        };
        Details.Children.Add(add);
    }
    private void TimerChange(Action action) {
        try { action(); timerSaveError = false; SetTimerView(true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { App.Log(ex); StatusText.Text = "Could not save boss timers. Your previous timers are unchanged."; }
    }
    private void DrawBossTimers() {
        TimerRows.Children.Clear(); countdowns.Clear();
        if (bossTimers.LoadFailed) TimerRows.Children.Add(new TextBlock { Text = "Saved timers could not be read. Check boss-timers.json before saving new timers, which will replace that file.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        if (bossTimers.Timers.Count == 0) TimerRows.Children.Add(new TextBlock { Text = "No boss timers yet.", FontSize = 16 });
        foreach (var timer in bossTimers.Timers.OrderBy(t => t.DueAt)) {
            var panel = new StackPanel { Margin = new Thickness(12) };
            TimerRows.Children.Add(new Border { Child = panel, Background = Brush("#FFFEF8"), BorderBrush = Brush("#B4C8DA"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 0, 10) });
            panel.Children.Add(new TextBlock { Text = $"{timer.BossName} · Channel {timer.Channel}", FontSize = 16, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = $"{timer.MapName} · Due {timer.DueAt.LocalDateTime:G}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 5) });
            var countdown = new TextBlock { FontSize = 16, Margin = new Thickness(0, 0, 0, 10) }; panel.Children.Add(countdown); countdowns.Add((timer, countdown));
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(actions);
            void Button(string label, Action action) { var button = new Button { Content = label }; button.Click += (_, _) => action(); actions.Children.Add(button); }
            Button("Killed now", () => TimerChange(() => bossTimers.Start(timer.BossId, timer.BossName, timer.MapId, timer.MapName, timer.Channel, timer.DurationSeconds, DateTimeOffset.UtcNow)));
            Button("Edit", () => {
                var map = new Entity { Kind = EntityKind.Map, Id = timer.MapId, Name = timer.MapName };
                var dialog = new BossTimerDialog(timer.BossName, [map], bossTimers, timer.BossId, timer) { Owner = this };
                if (dialog.ShowDialog() == true) TimerChange(() => bossTimers.Start(timer.BossId, timer.BossName, timer.MapId, timer.MapName, dialog.Channel, dialog.DurationSeconds, DateTimeOffset.UtcNow, timer.Id));
            });
            Button("Remove", () => TimerChange(() => bossTimers.Remove(timer.Id)));
        }
        UpdateCountdowns(DateTimeOffset.UtcNow);
    }
    private void UpdateCountdowns(DateTimeOffset now) {
        foreach (var (timer, text) in countdowns) {
            var remaining = timer.DueAt - now;
            text.Text = remaining <= TimeSpan.Zero ? "Respawn due · estimated" : $"{(long)Math.Ceiling(remaining.TotalSeconds) / 3600:00}:{(long)Math.Ceiling(remaining.TotalSeconds) / 60 % 60:00}:{(long)Math.Ceiling(remaining.TotalSeconds) % 60:00} remaining · estimated";
        }
    }
    private void CheckBossTimers() {
        var now = DateTimeOffset.UtcNow;
        try {
            var due = bossTimers.TakeDue(now);
            if (due.Count > 0) {
                var first = due[0];
                var message = due.Count == 1 ? $"{first.BossName} · {first.MapName} · Channel {first.Channel}" : $"{due.Count} boss respawn estimates are due. Open Boss timers for details.";
                tray.ShowAlert("Boss respawn due · estimated", message);
                if (settings.BossTimerSound) System.Media.SystemSounds.Asterisk.Play();
            }
            timerSaveError = false;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            if (!timerSaveError) { App.Log(ex); StatusText.Text = "Could not save boss timer notification state. Notifications will retry when storage is available."; }
            timerSaveError = true;
        }
        if (showingTimers && IsVisible) UpdateCountdowns(now);
    }
}
