using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MapleHelper.Core;
namespace MapleHelper;
public sealed class SettingsWindow : Window {
    private readonly TextBox root, shortcut;
    private readonly CheckBox rebuild, bossTimerSound;
    public bool BossTimerSound => bossTimerSound.IsChecked == true;
    public string GameRoot => root.Text.Trim();
    public string Shortcut => shortcut.Text.Trim();
    public bool Rebuild => rebuild.IsChecked == true;
    public SettingsWindow(Settings settings) {
        NameScope.SetNameScope(this, new NameScope());
        Title = "Field guide settings"; Width = 570; Height = 420; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Tahoma"); FontSize = 13; Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#FFF8E5")!;
        var panel = new StackPanel { Margin = new Thickness(22) }; Content = panel;
        panel.Children.Add(new TextBlock { Text = "Game folder", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 7) });
        root = new TextBox { Text = settings.GameRoot }; panel.Children.Add(root);
        var browse = new Button { Content = "Browse…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 16) };
        browse.Click += (_, _) => { var folder = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the MapleStory appdata folder" }; if (folder.ShowDialog(this) == true) root.Text = folder.FolderName; }; panel.Children.Add(browse);
        panel.Children.Add(new TextBlock { Text = "Open / close shortcut", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 7) });
        shortcut = new TextBox { Text = settings.Shortcut }; panel.Children.Add(shortcut);
        panel.Children.Add(new TextBlock { Text = "Use Ctrl, Shift, or Alt with a letter, number, Space, or function key (except F12).", TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 7, 0, 14) });
        rebuild = new CheckBox { Content = "Rebuild the local game-data cache", Margin = new Thickness(0, 0, 0, 17) }; panel.Children.Add(rebuild);
        bossTimerSound = new CheckBox { Content = "Play a sound when a boss timer is due", IsChecked = settings.BossTimerSound, Margin = new Thickness(0, 0, 0, 17) };
        RegisterName("BossTimerSoundBox", bossTimerSound); panel.Children.Add(bossTimerSound);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(cancel);
        var save = new Button { Content = "Save", IsDefault = true }; save.Click += (_, _) => {
            if (!Hotkey.TryParse(Shortcut, out _)) { MessageBox.Show(this, "Enter a shortcut such as Ctrl+Shift+Space or Alt+M."); return; }
            if ((Rebuild || !string.Equals(GameRoot, settings.GameRoot, StringComparison.OrdinalIgnoreCase)) && (!File.Exists(Path.Combine(GameRoot, "MapleStory.exe")) || !Directory.Exists(Path.Combine(GameRoot, "Data")))) { MessageBox.Show(this, "Select the appdata folder containing MapleStory.exe and Data."); return; }
            DialogResult = true;
        }; buttons.Children.Add(save); panel.Children.Add(buttons);
    }
}
