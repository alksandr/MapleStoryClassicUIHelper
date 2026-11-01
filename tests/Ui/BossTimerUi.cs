using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MapleHelper;
using MapleHelper.Core;

internal static class BossTimerUi {
    public static int Run() {
        var root = Path.Combine(Path.GetTempPath(), "maple-timer-ui-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("MAPLE_HELPER_DATA_ROOT", root);
        Directory.CreateDirectory(root);
        AtomicJson.Write(Path.Combine(root, "settings.json"), new Settings { GameRoot = Path.Combine(root, "missing"), BossTimerSound = false });
        var app = new App(hosted: true); app.InitializeComponent();
        var window = new MainWindow(); app.MainWindow = window;
        var failed = false;
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        timeout.Tick += (_, _) => { failed = true; Console.WriteLine("FAIL timer UI timed out"); timeout.Stop(); foreach (Window owned in window.OwnedWindows) owned.Close(); window.Quit(); };
        timeout.Start();
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
        object? Invoke(string method, params object[] args) => typeof(MainWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
        void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        BossTimerDialog? dialog = null;
        void FillDialog(int channel, int seconds, bool checkBlank = false) {
            var pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            pump.Tick += (_, _) => {
                dialog = window.OwnedWindows.OfType<BossTimerDialog>().FirstOrDefault();
                if (dialog == null) return;
                pump.Stop();
                if (checkBlank) {
                    try {
                        Check(((TextBox)dialog.FindName("HoursBox")).Text == "" && ((TextBox)dialog.FindName("MinutesBox")).Text == "" && ((TextBox)dialog.FindName("SecondsBox")).Text == "", "A new boss/map has blank duration fields");
                        Click((Button)dialog.FindName("SaveTimerButton"));
                        Check(dialog.IsVisible, "Blank duration keeps the dialog open for correction");
                    } catch (Exception ex) { failed = true; Console.WriteLine("FAIL " + ex); dialog.Close(); return; }
                }
                ((TextBox)dialog.FindName("ChannelBox")).Text = channel.ToString();
                ((TextBox)dialog.FindName("HoursBox")).Text = "0";
                ((TextBox)dialog.FindName("MinutesBox")).Text = "0";
                ((TextBox)dialog.FindName("SecondsBox")).Text = seconds.ToString();
                Click((Button)dialog.FindName("SaveTimerButton"));
            };
            pump.Start();
        }
        window.Loaded += async (_, _) => {
            try {
                await Task.Delay(200);
                BossTimerSuggestionUi.Run(window, root);
                var boss = new Entity { Kind = EntityKind.Monster, Id = 1, Name = "Test Boss", Facts = [new("Boss", "1")], Relations = [new(new(EntityKind.Map, 10), "Found in")] };
                var map = new Entity { Kind = EntityKind.Map, Id = 10, Name = "Test Map" };
                typeof(MainWindow).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, new CacheSnapshot(new Catalog { Records = [boss, map] }, root));
                var search = (TextBox)window.FindName("SearchBox"); search.Text = "Test Boss";
                await Task.Delay(200);
                Invoke("ShowEntity", boss);
                var details = (StackPanel)window.FindName("Details");
                var add = details.Children.OfType<Button>().Single(b => Equals(b.Content, "Add timer"));
                FillDialog(1, 30, checkBlank: true); Click(add);
                var path = Path.Combine(root, "boss-timers.json");
                Check(new BossTimers(path).Timers.Count == 1, "Add timer from boss details saves an isolated countdown");
                Check(((FrameworkElement)window.FindName("TimerView")).Visibility == Visibility.Visible, "Adding a timer opens timer view");
                Click((Button)window.FindName("TimersButton"));
                Check(search.Text == "Test Boss" && details.Children.Contains(add), "Lookup query and rendered details survive switching views");
                Click((Button)window.FindName("TimersButton"));
                var tray = typeof(MainWindow).GetField("tray", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var menu = tray.GetType().GetProperty("ContextMenuStrip")!.GetValue(tray)!;
                var menuItems = ((System.Collections.IEnumerable)menu.GetType().GetProperty("Items")!.GetValue(menu)!).Cast<object>();
                var fieldGuide = menuItems.Single(i => Equals(i.GetType().GetProperty("Text")!.GetValue(i), "Open field guide"));
                fieldGuide.GetType().GetMethod("PerformClick")!.Invoke(fieldGuide, null);
                Check(((FrameworkElement)window.FindName("TimerView")).Visibility == Visibility.Collapsed, "Open field guide tray command selects lookup after timers");
                Click((Button)window.FindName("TimersButton"));
                var focusSearch = window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.L && b.Modifiers == ModifierKeys.Control);
                ((RoutedCommand)focusSearch.Command).Execute(null, window);
                Check(search.IsKeyboardFocusWithin && search.SelectedText == "Test Boss", "Ctrl+L returns from timers to the retained search");
                Click((Button)window.FindName("TimersButton"));
                var rows = (StackPanel)window.FindName("TimerRows");
                Button Action(string label) => rows.Children.OfType<Border>().Select(b => (StackPanel)b.Child).SelectMany(p => p.Children.OfType<StackPanel>()).SelectMany(p => p.Children.OfType<Button>()).Single(b => Equals(b.Content, label));
                var oldDue = new BossTimers(path).Timers.Single().DueAt;
                Click(Action("Killed now"));
                Check(new BossTimers(path).Timers.Single().DueAt >= oldDue, "Killed now restarts the countdown");
                Directory.CreateDirectory("artifacts"); window.UpdateLayout();
                var render = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); render.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (var image = File.Create("artifacts/boss-timers-preview.png")) encoder.Save(image);
                FillDialog(2, 1); Click(Action("Edit"));
                Check(new BossTimers(path).Timers.Single().Channel == 2, "Edit changes channel and restarts");
                window.Dismiss();
                var foreground = GetForegroundWindow();
                await Task.Delay(2300);
                Check(!window.IsVisible && GetForegroundWindow() == foreground, "Hidden expiry leaves visibility and foreground unchanged");
                Check(new BossTimers(path).Timers.Single().Notified, "Shared timer loop persists notification receipt while hidden");
                window.OpenFromTray();
                Check(rows.Children.Count == 1, "Timer view remains available after reopening");
                Click(Action("Remove"));
                Check(new BossTimers(path).Timers.Count == 0, "Remove persists deletion");
                var settings = new SettingsWindow(new Settings { BossTimerSound = false });
                Check(((CheckBox)settings.FindName("BossTimerSoundBox")).IsChecked == false, "Settings reflects sound preference");
                var restarted = new BossTimers(path); restarted.Start(1, "Offline boss", 10, "Offline map", 3, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
                // A fresh window loads overdue state without needing client data.
                window.Hide();
                var second = new MainWindow();
                app.MainWindow = second; second.Show(); await Task.Delay(250);
                Check(new BossTimers(path).Timers.Single().Notified, "Startup consumes overdue timer once without client data");
                second.Quit();
                Console.WriteLine("PASS isolated boss timer UI flow");
            } catch (Exception ex) { failed = true; Console.WriteLine("FAIL " + ex); }
            finally { timeout.Stop(); window.Quit(); }
        };
        try { app.Run(window); return failed ? 1 : 0; }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
}
