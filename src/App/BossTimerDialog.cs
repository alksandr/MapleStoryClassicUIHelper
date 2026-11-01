using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using MapleHelper.Core;

namespace MapleHelper;
public sealed class BossTimerDialog : Window {
    private readonly ComboBox maps;
    private readonly TextBox channel, hours, minutes, seconds;
    public Entity Map => (Entity)((ComboBoxItem)maps.SelectedItem).Tag;
    public int Channel { get; private set; }
    public int DurationSeconds { get; private set; }
    public BossTimerDialog(string bossName, List<Entity> locations, BossTimers store, int bossId, BossTimer? editing = null, int? initialChannel = null) {
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
        channel = Input(panel, "Channel", "ChannelBox", (editing?.Channel ?? initialChannel ?? 1).ToString(CultureInfo.InvariantCulture));
        var durationRow = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(durationRow);
        TextBox Duration(string label, string name) { var column = new StackPanel { Width = 125 }; durationRow.Children.Add(column); return Input(column, label, name, ""); }
        hours = Duration("Hours", "HoursBox"); minutes = Duration("Minutes", "MinutesBox"); seconds = Duration("Seconds", "SecondsBox");
        var suggestionText = new TextBlock { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
        var suggestionCaveat = new TextBlock { FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        var suggestionSource = new TextBlock { FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };
        RegisterName("SuggestionText", suggestionText); RegisterName("SuggestionCaveat", suggestionCaveat); RegisterName("SuggestionSource", suggestionSource);
        panel.Children.Add(suggestionText); panel.Children.Add(suggestionCaveat); panel.Children.Add(suggestionSource);
        var drafts = new Dictionary<int, (string Hours, string Minutes, string Seconds)>();
        int? previousMap = null;
        BossTimerSuggestion? Resolve() => BossTimerSuggestions.Resolve(bossId, Map, store.RememberedSeconds(bossId, Map.Id), editing);
        void PrefillIfEmpty(BossTimerSuggestion? suggestion) {
            if (suggestion == null || !string.IsNullOrWhiteSpace(hours.Text) || !string.IsNullOrWhiteSpace(minutes.Text) || !string.IsNullOrWhiteSpace(seconds.Text)) return;
            hours.Text = (suggestion.Seconds / 3600).ToString(CultureInfo.InvariantCulture);
            minutes.Text = (suggestion.Seconds / 60 % 60).ToString(CultureInfo.InvariantCulture);
            seconds.Text = (suggestion.Seconds % 60).ToString(CultureInfo.InvariantCulture);
        }
        void SelectMap() {
            if (previousMap.HasValue) drafts[previousMap.Value] = (hours.Text, minutes.Text, seconds.Text);
            var draft = drafts.GetValueOrDefault(Map.Id, ("", "", ""));
            hours.Text = draft.Item1; minutes.Text = draft.Item2; seconds.Text = draft.Item3;
            var suggestion = Resolve();
            suggestionText.Text = suggestion == null ? "No known respawn time. Enter a duration." : $"Suggested duration: {suggestion.SourceLabel}";
            suggestionCaveat.Text = suggestion?.Caveat ?? "";
            suggestionSource.Inlines.Clear();
            if (suggestion?.SourceUrl is { } url) {
                var link = new Hyperlink(new Run("Community source")) { NavigateUri = new Uri(url) };
                link.RequestNavigate += (_, e) => {
                    e.Handled = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { App.Log(ex); suggestionCaveat.Text = "Could not open the community source in your browser."; }
                };
                suggestionSource.Inlines.Add(link);
                if (suggestion.VerifiedOn is { } date) suggestionSource.Inlines.Add(new Run($" · Verified {date:yyyy-MM-dd}"));
            }
            PrefillIfEmpty(suggestion);
            previousMap = Map.Id;
        }
        maps.SelectionChanged += (_, _) => SelectMap(); SelectMap();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 10, 0, 10) }; panel.Children.Add(error);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right }; panel.Children.Add(buttons);
        buttons.Children.Add(new Button { Content = "Cancel", IsCancel = true });
        var save = new Button { Content = editing == null ? "Start timer" : "Save and restart", IsDefault = true }; RegisterName("SaveTimerButton", save); buttons.Children.Add(save);
        save.Click += (_, _) => {
            PrefillIfEmpty(Resolve());
            bool Parse(TextBox box, out int value) => int.TryParse(string.IsNullOrWhiteSpace(box.Text) ? "0" : box.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value) && value >= 0;
            if (maps.SelectedItem == null || !Parse(channel, out var ch) || ch <= 0 || !Parse(hours, out var h) || !Parse(minutes, out var m) || m > 59 || !Parse(seconds, out var s) || s > 59 || (long)h * 3600 + m * 60 + s is <= 0 or > int.MaxValue) {
                error.Text = "Enter a positive channel and duration. Minutes and seconds must be 0–59."; return;
            }
            Channel = ch; DurationSeconds = (int)((long)h * 3600 + m * 60 + s); DialogResult = true;
        };
        Loaded += (_, _) => { var firstInput = initialChannel.HasValue ? channel : hours; firstInput.Focus(); firstInput.SelectAll(); };
    }
    private TextBox Input(StackPanel panel, string label, string name, string text) {
        var box = new TextBox { Text = text, Margin = new Thickness(0, 0, 8, 5) }; RegisterName(name, box); AutomationProperties.SetName(box, label);
        panel.Children.Add(new Label { Content = label, Target = box }); panel.Children.Add(box); return box;
    }
}
