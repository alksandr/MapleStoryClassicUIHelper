using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MapleHelper.Core;

namespace MapleHelper;
public sealed class BossTimerDialog : Window {
    private readonly ComboBox maps;
    private readonly TextBox channel, hours, minutes, seconds;
    public Entity Map => (Entity)((ComboBoxItem)maps.SelectedItem).Tag;
    public int Channel { get; private set; }
    public int DurationSeconds { get; private set; }
    public BossTimerDialog(string bossName, List<Entity> locations, BossTimers store, int bossId, BossTimer? editing = null) {
        NameScope.SetNameScope(this, new NameScope());
        Title = editing == null ? "Add boss timer" : "Edit boss timer";
        Width = 480; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.Cornsilk; FontFamily = new System.Windows.Media.FontFamily("Tahoma"); FontSize = 13;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = bossName, FontSize = 20, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = "Manual respawn estimate. Saving starts the countdown now.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
        maps = new ComboBox { ItemsSource = locations.Select(m => new ComboBoxItem { Content = $"{m.Name} · #{m.Id}", Tag = m }).ToList(), SelectedIndex = 0, IsEnabled = editing == null };
        RegisterName("MapChoice", maps);
        panel.Children.Add(new Label { Content = "Map", Target = maps }); panel.Children.Add(maps);
        channel = Input(panel, "Channel", "ChannelBox", (editing?.Channel ?? 1).ToString(CultureInfo.InvariantCulture));
        var durationRow = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(durationRow);
        TextBox Duration(string label, string name) { var column = new StackPanel { Width = 125 }; durationRow.Children.Add(column); return Input(column, label, name, ""); }
        hours = Duration("Hours", "HoursBox"); minutes = Duration("Minutes", "MinutesBox"); seconds = Duration("Seconds", "SecondsBox");
        void Prefill() {
            var value = editing?.DurationSeconds ?? store.RememberedSeconds(bossId, Map.Id);
            hours.Text = value.HasValue ? (value / 3600).ToString() : "";
            minutes.Text = value.HasValue ? (value / 60 % 60).ToString() : "";
            seconds.Text = value.HasValue ? (value % 60).ToString() : "";
        }
        maps.SelectionChanged += (_, _) => Prefill(); Prefill();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 10) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        var save = new Button { Content = editing == null ? "Start timer" : "Save and restart", IsDefault = true }; RegisterName("SaveTimerButton", save); buttons.Children.Add(save);
        save.Click += (_, _) => {
            bool Parse(TextBox box, out int value) => int.TryParse(string.IsNullOrWhiteSpace(box.Text) ? "0" : box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
            if (maps.SelectedItem == null || !Parse(channel, out var ch) || ch <= 0 || !Parse(hours, out var h) || !Parse(minutes, out var m) || m > 59 || !Parse(seconds, out var s) || s > 59 || (long)h * 3600 + m * 60 + s is <= 0 or > int.MaxValue) {
                error.Text = "Enter a positive channel and duration. Minutes and seconds must be 0–59."; return;
            }
            Channel = ch; DurationSeconds = (int)((long)h * 3600 + m * 60 + s); DialogResult = true;
        };
        Loaded += (_, _) => { hours.Focus(); hours.SelectAll(); };
    }
    private TextBox Input(StackPanel panel, string label, string name, string text) {
        var box = new TextBox { Text = text, Margin = new Thickness(0, 0, 8, 5) }; RegisterName(name, box); AutomationProperties.SetName(box, label);
        panel.Children.Add(new Label { Content = label, Target = box }); panel.Children.Add(box); return box;
    }
}
