using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using MapleHelper;
using MapleHelper.Core;

internal static class UiVerify {
    [STAThread] public static int Main(string[] args) {
        if (args.Contains("--boss-timers")) return BossTimerUi.Run();
        var missingGame = args.Contains("--missing-game");
        var placement = args.Contains("--placement");
        var settingsPath = Path.Combine(App.DataRoot, "settings.json");
        var settingsBytes = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
        var communityPath = Path.Combine(App.DataRoot, "community.json");
        var priorCommunityTime = File.GetLastWriteTimeUtc(communityPath);
        if (missingGame) AtomicJson.Write(settingsPath, new Settings { GameRoot = Path.Combine(Path.GetTempPath(), "missing-maple-" + Guid.NewGuid().ToString("N")) });
        if (placement) { var configured = AtomicJson.Read<Settings>(settingsPath) ?? new(); configured.OffsetX = 36; configured.OffsetY = 54; AtomicJson.Write(settingsPath, configured); }
        var app = new App(); app.InitializeComponent();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        var started = DateTime.UtcNow;
        var running = false;
        var failed = false;
        var completed = false;
        timer.Tick += async (_, _) => {
            if (running) return;
            if (DateTime.UtcNow - started > TimeSpan.FromMinutes(2)) { Console.WriteLine("FAIL UI startup timed out"); timer.Stop(); failed = true; (app.MainWindow as MainWindow)?.Quit(); return; }
            if (app.MainWindow is not MainWindow window || window.FindName("Results") is not ListBox results || results.Items.Count == 0) return;
            if (window.FindName("RefreshButton") is Button { IsEnabled: false }) return;
            running = true; timer.Stop();
            try {
                if (placement) { await Task.Delay(750); var configured = AtomicJson.Read<Settings>(settingsPath)!; Check(configured.OffsetX == 36 && configured.OffsetY == 54, "Automatic placement does not overwrite the saved game-relative position"); }
                var search = (TextBox)window.FindName("SearchBox");
                search.Text = "Slime";
                await Task.Delay(250);
                Check(results.Items[0] is ResultRow { Name: "Slime" }, "Exact search match is first");
                search.Focus();
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(results.SelectedIndex == 0, "Down selects first search result");
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                var details = (StackPanel)window.FindName("Details");
                Check(Texts(details).Contains("115"), "Monster stats render from installed client");
                Check(details.Children.OfType<Image>().Any(i => i.Source != null), "Monster artwork renders");
                Check(Texts(details).Contains("Best maps · estimated"), "Monster details include map ranking");
                var related = details.Children.OfType<Button>().First(b => b.Tag is EntityKey { Kind: EntityKind.Map });
                Check(related.Content is TextBlock rankedLabel && rankedLabel.Text.StartsWith("#1 · "), "Highest-scoring map is the first map link");
                related.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(details.Children.OfType<Image>().Any(i => i.Source != null), "Linked map minimap renders");
                ((Button)window.FindName("BackButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(Texts(details).Contains("Slime"), "Back returns to monster");
                ((Button)window.FindName("ClearSearchButton")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Check(search.Text.Length == 0 && search.IsKeyboardFocusWithin, "Clear search empties the query and returns focus");
                search.Text = "Slime";
                search.Select(2, 0); results.Focus();
                var searchShortcut = window.InputBindings.OfType<KeyBinding>().Single(b => b.Key == Key.L && b.Modifiers == ModifierKeys.Control);
                ((RoutedCommand)searchShortcut.Command).Execute(null, window);
                Check(search.IsKeyboardFocusWithin && search.SelectedText == search.Text && search.SelectionLength == 5, "Ctrl+L focuses search and selects the entire query from outside search");
                if (!missingGame) {
                    search.Text = "Blue Snail"; search.Focus();
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Check(Texts(details).Contains("Blue Snail"), "Enter immediately executes a pending search");
                    search.Text = "Red Snail"; search.Focus();
                    window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Down) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Check(results.SelectedItem is ResultRow { Name: "Red Snail" }, "Arrow navigation executes a pending search");
                }
                var dropCache = CommunityDrops.Load(communityPath)!;
                var report = dropCache.Drops.First();
                var monsterButton = ((StackPanel)window.FindName("Categories")).Children.OfType<Button>().Single(b => Equals(b.Tag, EntityKind.Monster));
                monsterButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                search.Text = report.Monster.Id.ToString(); await Task.Delay(250); results.SelectedIndex = 0;
                window.UpdateLayout();
                Check(Texts(details).Contains(report.ItemName), "Cached community reports render");
                if (missingGame) Check(priorCommunityTime == File.GetLastWriteTimeUtc(communityPath), "Missing game folder uses cached reports without an online refresh");
                if (!missingGame) {
                    var sharedItem = dropCache.Drops.Where(d => d.Item.HasValue).GroupBy(d => d.Item).First(g => g.Select(d => d.SourcePage).Distinct().Count() > 1);
                    var allButton = ((StackPanel)window.FindName("Categories")).Children.OfType<Button>().Single(b => b.Tag == null);
                    allButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    search.Text = sharedItem.Key!.Value.Id.ToString(); await Task.Delay(250);
                    results.SelectedItem = results.Items.Cast<ResultRow>().First(r => r.Entity.Key == sharedItem.Key);
                    var links = details.Children.OfType<TextBlock>().SelectMany(t => t.Inlines.OfType<Hyperlink>()).Count();
                    Check(links >= sharedItem.Select(d => d.SourcePage).Distinct().Count(), "Each community source has its own link");
                    search.Text = "Slime"; await Task.Delay(250); results.SelectedIndex = 0;
                }
                Directory.CreateDirectory("artifacts");
                window.UpdateLayout();
                var render = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                render.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(render));
                using (var stream = File.Create("artifacts/helper-preview.png")) encoder.Save(stream);
                if (!missingGame) {
                    var rankingHeader = details.Children.OfType<TextBlock>().Single(t => t.Text == "Best maps · estimated");
                    ((ScrollViewer)window.FindName("DetailScroll")).ScrollToVerticalOffset(rankingHeader.TranslatePoint(new Point(), details).Y);
                    window.UpdateLayout();
                    var rankingRender = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                    rankingRender.Render(window);
                    var rankingEncoder = new PngBitmapEncoder(); rankingEncoder.Frames.Add(BitmapFrame.Create(rankingRender));
                    using var stream = File.Create("artifacts/map-ranking-preview.png"); rankingEncoder.Save(stream);
                }
                window.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Check(!window.IsVisible, "Escape hides the helper");
                var wasGameForeground = IsGameForeground();
                window.OpenFromTray();
                if (wasGameForeground) Check(window.Topmost, "Reopening over foreground MapleStory stays above the game");
                if (placement) { await Task.Delay(750); var configured = AtomicJson.Read<Settings>(settingsPath)!; Check(configured.OffsetX == 36 && configured.OffsetY == 54, "Reopening does not overwrite the saved game-relative position"); }
                if (placement && wasGameForeground) {
                    window.Left += 20; window.Top += 10;
                    await Task.Delay(750);
                    var configured = AtomicJson.Read<Settings>(settingsPath)!;
                    Check(configured.OffsetX == 36 && configured.OffsetY == 54, "Programmatic moves do not overwrite the saved game-relative position");
                }
                Check(window.ShowInTaskbar, "The visible helper can be found in the taskbar");
                Check(window.IsVisible && search.Text == (missingGame ? report.Monster.Id.ToString() : "Slime"), "Reopen preserves search");
                search.Select(2, 0);
                window.Dismiss();
                var helperHandle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                SendMessage(helperHandle, 0x0312, new IntPtr(17), IntPtr.Zero);
                Check(window.IsVisible && search.IsKeyboardFocusWithin && search.SelectionLength == search.Text.Length, "Hotkey reopen focuses search and selects the entire retained query");
                SendMessage(helperHandle, 0x0312, new IntPtr(17), IntPtr.Zero);
                Check(!window.IsVisible, "Hotkey still toggles the visible panel closed");
                Console.WriteLine("PASS UI flow and rendered preview");
                completed = true;
            } catch (Exception ex) { failed = true; Console.WriteLine("FAIL " + ex); }
            window.Quit();
        };
        try { timer.Start(); app.Run(); return failed || !completed ? 1 : 0; }
        finally { if (missingGame || placement) { if (settingsBytes == null) File.Delete(settingsPath); else File.WriteAllBytes(settingsPath, settingsBytes); } }
    }
    private static IEnumerable<string> Texts(DependencyObject root) {
        if (root is TextBlock text) yield return text.Text;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var value in Texts(VisualTreeHelper.GetChild(root, i))) yield return value;
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    private static bool IsGameForeground() {
        GetWindowThreadProcessId(GetForegroundWindow(), out var id);
        try { using var process = System.Diagnostics.Process.GetProcessById((int)id); return process.ProcessName == "MapleStory"; } catch { return false; }
    }
}
