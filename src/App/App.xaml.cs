using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace MapleHelper;
public partial class App : Application {
    public static readonly string DataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MapleStoryClassicUIHelper");
    private Mutex? mutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? showWait;
    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        Directory.CreateDirectory(DataRoot);
        mutex = new Mutex(true, @"Local\MapleStoryClassicUIHelper", out var created);
        if (!created) {
            try { using var signal = EventWaitHandle.OpenExisting(@"Local\MapleStoryClassicUIHelper.Show"); signal.Set(); } catch (WaitHandleCannotBeOpenedException) { MessageBox.Show("The helper is already running. Open it from the system tray."); }
            Shutdown(); return;
        }
        File.WriteAllText(Path.Combine(DataRoot, "session.log"), $"Started {DateTimeOffset.Now:O}\n");
        DispatcherUnhandledException += (_, error) => { Log(error.Exception); MessageBox.Show(error.Exception.Message, "Maple helper error"); error.Handled = true; Shutdown(1); };
        AppDomain.CurrentDomain.UnhandledException += (_, error) => { if (error.ExceptionObject is Exception ex) Log(ex); };
        var window = new MainWindow();
        MainWindow = window;
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\MapleStoryClassicUIHelper.Show");
        showWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(new Action(window.OpenFromTray)), null, Timeout.Infinite, false);
        window.Show();
    }
    public static void Log(Exception ex) { try { File.AppendAllText(Path.Combine(DataRoot, "session.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch (IOException) { } }
    protected override void OnExit(ExitEventArgs e) { showWait?.Unregister(null); showEvent?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
}
