using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MapleHelper.Core;
using Forms = System.Windows.Forms;

namespace MapleHelper;
public sealed record ResultRow(Entity Entity, BitmapSource? Icon) {
    public string Name => Entity.Name;
    public string Subtitle => $"{Entity.Kind} #{Entity.Id}" + (Entity.Facts.FirstOrDefault(f => f.Label == "Level") is { } level ? $" · Lv. {level.Value}" : "");
}
public partial class MainWindow : Window {
    private readonly string settingsPath = Path.Combine(App.DataRoot, "settings.json");
    private readonly string dropsPath = Path.Combine(App.DataRoot, "community.json");
    private readonly Settings settings;
    private readonly CacheStore cache = new(Path.Combine(App.DataRoot, "cache"));
    private CacheSnapshot? snapshot;
    private CommunitySnapshot? community;
    private CancellationTokenSource? importCancellation;
    private Task? loading;
    private bool importing, refreshing, exiting, followGame, wanted, registered;
    private EntityKind? category;
    private Entity? current;
    private readonly Stack<EntityKey> history = [];
    private readonly Dictionary<string, BitmapSource?> images = [];
    private readonly DispatcherTimer searchTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private readonly DispatcherTimer windowTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly CancellationTokenSource lifetime = new();
    private readonly Forms.NotifyIcon tray;
    private IntPtr handle, game;
    private Native.Rect lastBounds;
    private DateTime nextDiscovery;

    public MainWindow() {
        settings = AtomicJson.Read<Settings>(settingsPath) ?? new();
        if (!Hotkey.TryParse(settings.Shortcut, out _)) settings.Shortcut = "Ctrl+Shift+Space";
        game = Native.FindGameWindow();
        followGame = game != IntPtr.Zero && Native.GetForegroundWindow() == game;
        wanted = true;
        InitializeComponent();
        var focusSearch = new RoutedCommand();
        InputBindings.Add(new KeyBinding(focusSearch, Key.L, ModifierKeys.Control));
        CommandBindings.Add(new CommandBinding(focusSearch, (_, e) => { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }));
        Topmost = followGame;
        Width = double.IsFinite(settings.Width) ? Math.Clamp(settings.Width, 700, Math.Max(700, SystemParameters.WorkArea.Width)) : 900;
        Height = double.IsFinite(settings.Height) ? Math.Clamp(settings.Height, 450, Math.Max(450, SystemParameters.WorkArea.Height)) : 640;
        Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
        Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
        if (!double.IsFinite(settings.OffsetX)) settings.OffsetX = 36;
        if (!double.IsFinite(settings.OffsetY)) settings.OffsetY = 54;
        ShortcutText.Text = $"{settings.Shortcut} opens the guide. Esc returns to MapleStory.";
        AddCategories();
        Welcome();
        searchTimer.Tick += (_, _) => { searchTimer.Stop(); UpdateResults(); };
        windowTimer.Tick += (_, _) => TrackGame();
        saveTimer.Tick += (_, _) => { saveTimer.Stop(); SaveSettings(); };
        SizeChanged += (_, _) => { settings.Width = Width; settings.Height = Height; saveTimer.Stop(); saveTimer.Start(); };
        SourceInitialized += (_, _) => { handle = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(handle)?.AddHook(WindowMessage); if (followGame) PositionOverGame(); windowTimer.Start(); };
        Closing += OnClosing;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MapleStoryClassicUIHelper/1.0");
        tray = new Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Information, Text = "Maple Field Guide", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open field guide", null, (_, _) => Dispatcher.Invoke(OpenFromTray));
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => { OpenFromTray(); OpenSettings(this, new RoutedEventArgs()); }));
        menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(Quit));
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenFromTray);
        Loaded += (_, _) => { loading = LoadLibrary(false); SearchBox.Focus(); };
    }
    private async Task LoadLibrary(bool force) {
        if (importing) return;
        importing = true;
        CancelButton.Visibility = Visibility.Visible;
        RefreshButton.IsEnabled = false;
        importCancellation = new CancellationTokenSource();
        var token = importCancellation.Token;
        try {
            var cached = cache.Load();
            community = CommunityDrops.Load(dropsPath);
            if (cached != null && snapshot == null) UseSnapshot(cached);
            var fingerprint = await Task.Run(() => ArchiveImporter.Fingerprint(settings.GameRoot), token);
            if (force || snapshot?.Catalog.Fingerprint != fingerprint) {
                StatusText.Text = "Importing game data. You can cancel without losing the previous guide.";
                var root = settings.GameRoot;
                var progress = new Progress<string>(message => StatusText.Text = message);
                var built = await cache.RebuildAsync(fingerprint, dir => new ArchiveImporter().Import(root, dir, progress, token), () => ArchiveImporter.Fingerprint(root), token);
                UseSnapshot(built);
            }
            ReadyStatus();
        } catch (OperationCanceledException) { StatusText.Text = snapshot == null ? "Import cancelled. Open Settings to retry." : "Import cancelled. Your previous guide is available."; }
        catch (Exception ex) { App.Log(ex); StatusText.Text = $"{ex.Message} Open Settings to check the game folder or retry."; }
        finally { importing = false; CancelButton.Visibility = Visibility.Collapsed; RefreshButton.IsEnabled = snapshot != null; importCancellation.Dispose(); importCancellation = null; }
        if (snapshot != null && community == null && !token.IsCancellationRequested) await RefreshCommunity();
    }
    private void UseSnapshot(CacheSnapshot value) {
        snapshot = value; images.Clear(); history.Clear(); current = null;
        UpdateResults(); Welcome();
    }
    private void ReadyStatus() {
        if (snapshot == null) return;
        StatusText.Text = $"{snapshot.Catalog.Records.Count:N0} records · Client data imported {snapshot.Catalog.ImportedAt.LocalDateTime:g}";
        if (snapshot.Catalog.Warnings.Count > 0) StatusText.Text += " · Some artwork is unavailable";
    }
    private void AddCategories() {
        foreach (EntityKind? kind in new EntityKind?[] { null, EntityKind.Item, EntityKind.Equipment, EntityKind.Monster, EntityKind.Map, EntityKind.Npc, EntityKind.Quest, EntityKind.Skill }) {
            var button = new Button { Content = kind switch { null => "All", EntityKind.Npc => "NPCs", EntityKind.Equipment => "Equipment", _ => kind + "s" }, Tag = kind, Padding = new Thickness(9, 5, 9, 5), FontSize = 12 };
            button.Click += (_, _) => { category = kind; UpdateCategoryStyle(); UpdateResults(); SearchBox.Focus(); };
            Categories.Children.Add(button);
        }
        UpdateCategoryStyle();
    }
    private void UpdateCategoryStyle() {
        foreach (Button button in Categories.Children) { var selected = Equals(button.Tag, category); button.Background = Brush(selected ? "#CCE4F9" : "#FFF8E5"); button.FontWeight = selected ? FontWeights.Bold : FontWeights.Normal; }
    }
    private void SearchChanged(object sender, TextChangedEventArgs e) { if (searchTimer == null) return; searchTimer.Stop(); searchTimer.Start(); }
    private void ClearSearchClick(object sender, RoutedEventArgs e) { SearchBox.Clear(); searchTimer.Stop(); UpdateResults(); SearchBox.Focus(); }
    private void SearchClick(object sender, RoutedEventArgs e) { searchTimer.Stop(); UpdateResults(); if (Results.Items.Count > 0) Results.SelectedIndex = 0; }
    private void UpdateResults() {
        if (snapshot == null) return;
        var found = snapshot.Catalog.Search(SearchBox.Text, category);
        Results.ItemsSource = found.Select(entity => new ResultRow(entity, ImageFor(entity.Image, true))).ToList();
        ResultsCount.Text = found.Count == 200 ? "200 shown · type to narrow your search" : $"{found.Count:N0} results";
        if (found.Count == 0) { Details.Children.Clear(); Text("No matching records", 21, true); Text("Try a shorter name, another category, or a numeric game ID."); }
    }
    private void ResultSelected(object sender, SelectionChangedEventArgs e) {
        if (Results.SelectedItem is ResultRow row) { history.Clear(); ShowEntity(row.Entity); }
    }
    private void Welcome() {
        Details.Children.Clear();
        Text("What are you looking for?", 23, true);
        Text("Search items, equipment, monsters, maps, NPCs, quests, or skills. Select a result to see its details.", 14);
        Text("Follow the connections", 16, true);
        Text("Open a monster to find its maps. Open a map to see its monsters, NPCs, and exits. Quest records link to the people and items you need.");
        Text("Use ↑ / ↓ to choose a result and Enter to open it. Esc closes the guide.");
    }
    private void ShowEntity(Entity entity) {
        current = entity; BackButton.IsEnabled = history.Count > 0;
        Details.Children.Clear(); DetailScroll.ScrollToTop();
        Text(entity.Kind == EntityKind.Npc ? "NPC" : entity.Kind.ToString(), 11);
        Text(entity.Name, 23, true);
        Text($"ID {entity.Id}", 11);
        if (ImageFor(entity.Image, false) is { } bitmap) {
            var artwork = new Image { Source = bitmap, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, MaxHeight = entity.Kind == EntityKind.Map ? 210 : Math.Min(96, bitmap.PixelHeight * 2), MaxWidth = 440, Margin = new Thickness(0, 8, 0, 12), SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(artwork, BitmapScalingMode.NearestNeighbor); Details.Children.Add(artwork);
        }
        else Text("Artwork unavailable", 11);
        if (entity.Description.Length > 0) Text(entity.Description, 13);
        var stats = entity.Facts.Where(f => !f.Label.StartsWith("Level ")).ToList();
        if (stats.Count > 0) {
            var grid = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
            foreach (var fact in stats) { var box = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 8, 10) }; box.Children.Add(new TextBlock { Text = fact.Label, FontSize = 11, Foreground = Brush("#586C7D"), TextWrapping = TextWrapping.Wrap }); box.Children.Add(new TextBlock { Text = fact.Value, FontSize = 14, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }); grid.Children.Add(box); }
            Details.Children.Add(grid);
        }
        var rankedMaps = entity.Kind == EntityKind.Monster && snapshot != null ? MapRanking.ForMonster(snapshot.Catalog, entity.Key) : [];
        if (entity.Kind == EntityKind.Monster) DrawRankedMaps(rankedMaps);
        var rankedKeys = rankedMaps.Select(r => r.Map.Key).ToHashSet();
        foreach (var group in entity.Relations.Where(r => !rankedKeys.Contains(r.Target)).GroupBy(r => r.Label)) {
            Text(group.Key, 15, true);
            foreach (var relation in group) if (snapshot?.Catalog.Find(relation.Target) is { } related) EntityButton(related, related.Name);
        }
        foreach (var fact in entity.Facts.Where(f => f.Label.StartsWith("Level "))) { Text(fact.Label, 13, true); Text(fact.Value); }
        if (entity.Kind is EntityKind.Monster or EntityKind.Item or EntityKind.Equipment) DrawDrops(entity);
        Text("Source: your installed Classic World client. Client content may include records that are not currently obtainable.", 10);
    }
    private void DrawRankedMaps(List<RankedMap> maps) {
        Text("Best maps · estimated", 15, true);
        if (maps.Count == 0) { Text("No repeatable client spawn placements are available to rank. Other known locations are listed below.", 12); return; }
        Text("Score: 45% spawn count + 35% density + 20% layout. Count and density are relative to this monster’s maps. Layout favors less horizontal spread and climbing.", 11);
        Text("Client placements, not live population or kills/hour. Density uses the full map bounds; layout uses spawn coordinates, not reachable routes. Timed spawns count equally; server caps, respawn speed, class mobility, and competition are not measured.", 10);
        var rank = 0;
        foreach (var map in maps) {
            var label = map.Score.HasValue ? $"#{++rank} · {map.Map.Name} · {map.Score.Value:0.0}/100" : $"{map.Map.Name} · Unranked";
            EntityButton(map.Map, label);
            Text($"{map.SpawnCount} target placements / {map.TotalSpawns} total ({100d * map.SpawnCount / map.TotalSpawns:0}% target)" + (map.TimedSpawns > 0 ? $" · {map.TimedSpawns} timed" : ""), 12);
            Text((map.Density.HasValue ? $"Density: {map.Density.Value:0.##} targets / 1M px²" : "Density: unknown map bounds") +
                 (map.Layout.HasValue ? $" · Target spread: {map.SpreadX:0} × {map.SpreadY:0} px · Layout: {100 * map.Layout:0}/100" : " · Layout: unknown coordinates"), 11);
        }
    }
    private void DrawDrops(Entity entity) {
        Text(entity.Kind == EntityKind.Monster ? "Community drop reports" : "Reported drop sources", 15, true);
        var rows = community?.Drops.Where(d => (d.Monster == entity.Key || d.Item == entity.Key) && snapshot?.Catalog.Find(d.Monster)?.Name == d.MonsterName && (d.Item == null || snapshot.Catalog.Find(d.Item.Value)?.Name == d.ItemName)).ToList() ?? [];
        if (rows.Count == 0) { Text("No verified community reports are cached for this record. This does not mean it has no drops.", 12); return; }
        foreach (var drop in rows) {
            var target = entity.Kind == EntityKind.Monster ? drop.Item : drop.Monster;
            var name = entity.Kind == EntityKind.Monster ? drop.ItemName : drop.MonsterName;
            var rate = drop.Rate.HasValue ? drop.Rate.Value.ToString("0.####", CultureInfo.InvariantCulture) + "% observed" : "Unknown rate";
            if (target.HasValue && snapshot!.Catalog.Find(target.Value) is { } related) EntityButton(related, name);
            else Text(name, 13, true);
            Text($"{rate} · Quantity: {drop.Quantity}" + (drop.Version.Length > 0 ? $" · Version {drop.Version}" : ""), 11);
            Link($"Source: {drop.SourcePage} · MapleClassic Wiki", "https://mapleclassic.wiki/wiki/" + Uri.EscapeDataString(drop.SourcePage.Replace(' ', '_')));
        }
        Text($"Community observations may describe an older version. Retrieved {community!.RetrievedAt.LocalDateTime:g}.", 10);
        Link("Community data · CC BY-NC-SA 4.0", "https://creativecommons.org/licenses/by-nc-sa/4.0/");
    }
    private void EntityButton(Entity entity, string name) {
        var button = new Button { Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, Tag = entity.Key, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9, 5, 9, 5), Margin = new Thickness(0, 0, 0, 5) };
        button.Click += (_, _) => { if (current != null) history.Push(current.Key); ShowEntity(entity); };
        Details.Children.Add(button);
    }
    private void BackClick(object sender, RoutedEventArgs e) { if (history.TryPop(out var key) && snapshot?.Catalog.Find(key) is { } entity) ShowEntity(entity); }
    private void Text(string value, double size = 13, bool bold = false) => Details.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, Margin = new Thickness(0, bold ? 9 : 0, 0, 9), Foreground = Brush(size <= 11 ? "#586C7D" : "#23354A") });
    private void Link(string label, string url) {
        var text = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 12) };
        var link = new Hyperlink(new Run(label)) { NavigateUri = new Uri(url) };
        link.Click += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { App.Log(ex); StatusText.Text = "Could not open the browser."; } };
        text.Inlines.Add(link); Details.Children.Add(text);
    }
    private BitmapSource? ImageFor(string? relative, bool thumbnail) {
        if (relative == null || snapshot == null) return null;
        var key = relative + (thumbnail ? ":thumb" : ":full");
        if (images.TryGetValue(key, out var cached)) return cached;
        try {
            var path = Path.GetFullPath(Path.Combine(snapshot.Directory, relative));
            if (!path.StartsWith(snapshot.Directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            using var file = File.OpenRead(path);
            var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = file;
            if (thumbnail) bitmap.DecodePixelWidth = 48;
            bitmap.EndInit(); bitmap.Freeze(); images[key] = bitmap; return bitmap;
        } catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException) { images[key] = null; return null; }
    }
    private static SolidColorBrush Brush(string color) => new((Color)ColorConverter.ConvertFromString(color));
    private void CancelImport(object sender, RoutedEventArgs e) => importCancellation?.Cancel();
    private async void RefreshDrops(object sender, RoutedEventArgs e) => await RefreshCommunity();
    private async Task RefreshCommunity() {
        if (snapshot == null || refreshing) return;
        refreshing = true; RefreshButton.IsEnabled = false;
        StatusText.Text = "Refreshing community drop reports… Your local guide is available.";
        try { community = await new CommunityDrops(http).RefreshAsync(snapshot.Catalog, dropsPath, lifetime.Token); StatusText.Text = $"{community.Drops.Count:N0} verified community reports · Retrieved {community.RetrievedAt.LocalDateTime:g}"; if (current != null) ShowEntity(current); }
        catch (Exception ex) { App.Log(ex); StatusText.Text = "Community reports could not be refreshed. Cached reports and client data remain available."; }
        finally { refreshing = false; RefreshButton.IsEnabled = !importing && snapshot != null; }
    }
    private void OnKeyDown(object sender, KeyEventArgs e) {
        if ((e.Key is Key.Down or Key.Up or Key.Enter) && (SearchBox.IsKeyboardFocusWithin || Results.IsKeyboardFocusWithin) && searchTimer.IsEnabled) { searchTimer.Stop(); UpdateResults(); }
        if (e.Key == Key.Escape) { Dismiss(); e.Handled = true; }
        else if (e.Key is Key.Down or Key.Up && (SearchBox.IsKeyboardFocusWithin || Results.IsKeyboardFocusWithin) && Results.Items.Count > 0) { Results.SelectedIndex = Math.Clamp(Results.SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, Results.Items.Count - 1); Results.ScrollIntoView(Results.SelectedItem); e.Handled = true; }
        else if (e.Key == Key.Enter && (SearchBox.IsKeyboardFocusWithin || Results.IsKeyboardFocusWithin)) { if (Results.SelectedIndex < 0 && Results.Items.Count > 0) Results.SelectedIndex = 0; if (Results.SelectedItem is ResultRow row) { ShowEntity(row.Entity); DetailScroll.Focus(); } e.Handled = true; }
    }
    private void DragTitle(object sender, MouseButtonEventArgs e) { if (e.OriginalSource is System.Windows.Controls.Primitives.ButtonBase || e.OriginalSource is TextBlock { Parent: Button }) return; if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    public void OpenFromTray() { if (!Native.IsWindow(game)) game = Native.FindGameWindow(); followGame = game != IntPtr.Zero && Native.GetForegroundWindow() == game; wanted = true; Topmost = followGame; if (followGame) PositionOverGame(); Show(); Activate(); SearchBox.Focus(); }
    private void ToggleOverlay() {
        if (!IsEnabled) return;
        if (IsVisible) { Dismiss(); return; }
        followGame = game != IntPtr.Zero && Native.GetForegroundWindow() == game;
        wanted = true; Topmost = followGame;
        if (followGame) PositionOverGame();
        Show(); Activate(); SearchBox.Focus(); SearchBox.SelectAll();
    }
    public void Dismiss() { wanted = false; var restore = Native.IsOurWindow(Native.GetForegroundWindow()); Hide(); if (restore && game != IntPtr.Zero && Native.IsWindow(game) && !Native.IsIconic(game)) Native.SetForegroundWindow(game); }
    private void HideClick(object sender, RoutedEventArgs e) => Dismiss();
    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wparam, IntPtr lparam, ref bool handled) {
        if (message == 0x0312 && wparam.ToInt32() == 17) { ToggleOverlay(); handled = true; }
        // Save only completed user moves/resizes; WPF also moves windows during startup and reopening.
        else if (message == 0x0232) RememberPosition();
        return IntPtr.Zero;
    }
    private void TrackGame() {
        if (!Native.IsWindow(game)) game = IntPtr.Zero;
        if (DateTime.UtcNow >= nextDiscovery) { var found = Native.FindGameWindow(); if (game != found) { game = found; lastBounds = default; } nextDiscovery = DateTime.UtcNow.AddSeconds(1); }
        var foreground = Native.GetForegroundWindow();
        var allowed = Native.IsOurWindow(foreground) || game != IntPtr.Zero && foreground == game;
        if (allowed && !registered && Hotkey.TryParse(settings.Shortcut, out var hotkey)) { registered = Native.RegisterHotKey(handle, 17, hotkey.Modifiers | 0x4000, hotkey.Key); if (!registered) ShortcutText.Text = "Shortcut is in use. Change it in Settings; the tray icon still opens the guide."; }
        else if (!allowed && registered) { Native.UnregisterHotKey(handle, 17); registered = false; }
        if (!followGame || !wanted) return;
        if (game == IntPtr.Zero || Native.IsIconic(game) || !allowed) { if (IsVisible) Hide(); return; }
        var rect = Native.GameBounds(game);
        if (!rect.Equals(lastBounds)) { lastBounds = rect; PositionOverGame(); }
        if (!IsVisible && foreground == game) { ShowActivated = false; Topmost = true; Show(); ShowActivated = true; }
    }
    private void PositionOverGame() {
        if (game == IntPtr.Zero || handle == IntPtr.Zero) return;
        var rect = Native.GameBounds(game); var dpi = VisualTreeHelper.GetDpi(this);
        var left = rect.Left + (int)(settings.OffsetX * dpi.DpiScaleX); var top = rect.Top + (int)(settings.OffsetY * dpi.DpiScaleY);
        var screen = Forms.Screen.FromHandle(game).WorkingArea;
        left = Math.Clamp(left, screen.Left, Math.Max(screen.Left, screen.Right - (int)(Width * dpi.DpiScaleX)));
        top = Math.Clamp(top, screen.Top, Math.Max(screen.Top, screen.Bottom - (int)(Height * dpi.DpiScaleY)));
        Native.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    private void RememberPosition() {
        if (!followGame || game == IntPtr.Zero || handle == IntPtr.Zero) return;
        var rect = Native.GameBounds(game); var dpi = VisualTreeHelper.GetDpi(this);
        if (Native.GetWindowRect(handle, out var own)) { settings.OffsetX = (own.Left - rect.Left) / dpi.DpiScaleX; settings.OffsetY = (own.Top - rect.Top) / dpi.DpiScaleY; saveTimer.Stop(); saveTimer.Start(); }
    }
    private void SaveSettings() { try { AtomicJson.Write(settingsPath, settings); } catch (Exception ex) { App.Log(ex); StatusText.Text = "Could not save settings. Check access to your local application-data folder."; } }
    private void OpenSettings(object sender, RoutedEventArgs e) {
        if (importing) { StatusText.Text = "Cancel the current import before changing settings."; return; }
        var dialog = new SettingsWindow(settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (registered) { Native.UnregisterHotKey(handle, 17); registered = false; }
        if (!Hotkey.TryParse(dialog.Shortcut, out var hotkey) || !Native.RegisterHotKey(handle, 17, hotkey.Modifiers | 0x4000, hotkey.Key)) { MessageBox.Show(this, "That shortcut is already in use. Your previous shortcut is unchanged.", "Choose another shortcut"); TrackGame(); return; }
        registered = true; settings.Shortcut = dialog.Shortcut; settings.GameRoot = dialog.GameRoot;
        ShortcutText.Text = $"{settings.Shortcut} opens the guide. Esc returns to MapleStory.";
        SaveSettings(); loading = LoadLibrary(dialog.Rebuild);
    }
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Dismiss(); } }
    public async void Quit() {
        if (exiting) return;
        exiting = true; importCancellation?.Cancel(); lifetime.Cancel();
        if (loading != null) await loading;
        SaveSettings(); windowTimer.Stop(); searchTimer.Stop(); saveTimer.Stop();
        if (registered) Native.UnregisterHotKey(handle, 17);
        tray.Visible = false; tray.Dispose(); http.Dispose(); lifetime.Dispose(); Close(); Application.Current.Shutdown();
    }
}
