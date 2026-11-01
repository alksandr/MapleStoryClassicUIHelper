using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace MapleHelper;

// Own the tray icon so balloons can always use NIIF_NOSOUND. The app controls sound
// separately; Windows' default balloon sound would otherwise bypass the mute setting.
internal sealed class TrayIcon : Forms.NativeWindow, IDisposable {
    private const int Callback = 0x8001;
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private bool visible;
    public Icon Icon { get; set; } = SystemIcons.Information;
    public string Text { get; set; } = "Maple Field Guide";
    public Forms.ContextMenuStrip? ContextMenuStrip { get; set; }
    public event EventHandler? DoubleClick;
    public bool Visible {
        get => visible;
        set { if (visible == value) return; visible = value; Send(value ? 0u : 2u, 1 | 2 | 4); }
    }
    public TrayIcon() { CreateHandle(new Forms.CreateParams { Caption = "Maple Field Guide tray" }); }
    public void ShowAlert(string title, string text) => Send(1, 0x10, title, text);
    private void Send(uint operation, uint flags, string title = "", string text = "") {
        var data = new NotifyData {
            Size = (uint)Marshal.SizeOf<NotifyData>(), Window = Handle, Id = 1, Flags = flags,
            CallbackMessage = Callback, Icon = Icon.Handle, Tip = Text,
            Info = text.Length > 255 ? text[..252] + "…" : text,
            InfoTitle = title, InfoFlags = 1 | 0x10 // NIIF_INFO | NIIF_NOSOUND
        };
        ShellNotifyIcon(operation, ref data);
    }
    protected override void WndProc(ref Forms.Message message) {
        if (message.Msg == taskbarCreated && visible) Send(0, 1 | 2 | 4);
        if (message.Msg == Callback) {
            var action = message.LParam.ToInt32() & 0xffff;
            if (action == 0x203 || action == 0x405) DoubleClick?.Invoke(this, EventArgs.Empty);
            else if (action == 0x205 && ContextMenuStrip != null) {
                SetForegroundWindow(Handle);
                ContextMenuStrip.Show(Forms.Cursor.Position);
                PostMessage(Handle, 0, IntPtr.Zero, IntPtr.Zero);
            }
        }
        base.WndProc(ref message);
    }
    public void Dispose() { Visible = false; ContextMenuStrip?.Dispose(); DestroyHandle(); }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, CallbackMessage;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint operation, ref NotifyData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
}
