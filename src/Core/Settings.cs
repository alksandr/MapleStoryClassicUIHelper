namespace MapleHelper.Core;
public sealed class Settings {
    public string GameRoot { get; set; } = @"C:\Nexon\Library\maplestorycw\appdata";
    public string Shortcut { get; set; } = "Ctrl+Shift+Space";
    public bool BossTimerSound { get; set; } = true;
    public double Width { get; set; } = 900;
    public double Height { get; set; } = 640;
    public double OffsetX { get; set; } = 36;
    public double OffsetY { get; set; } = 54;
}
