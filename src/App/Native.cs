using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
namespace MapleHelper;
internal static class Native {
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    private delegate bool EnumCallback(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumCallback callback, IntPtr param);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
    internal static IntPtr FindGameWindow() {
        var processes = Process.GetProcessesByName("MapleStory");
        try {
            var ids = processes.Select(p => (uint)p.Id).ToHashSet();
            var found = IntPtr.Zero;
            EnumWindows((window, _) => { GetWindowThreadProcessId(window, out var pid); if (!ids.Contains(pid) || !IsWindowVisible(window)) return true; found = window; return false; }, IntPtr.Zero);
            return found;
        } finally { foreach (var process in processes) process.Dispose(); }
    }
    internal static Rect GameBounds(IntPtr window) {
        if (GetClientRect(window, out var rect)) { var origin = new Point(); if (ClientToScreen(window, ref origin)) return new Rect { Left = origin.X, Top = origin.Y, Right = origin.X + rect.Right, Bottom = origin.Y + rect.Bottom }; }
        GetWindowRect(window, out var bounds); return bounds;
    }
    internal static bool IsOurWindow(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid == Environment.ProcessId; }
}
