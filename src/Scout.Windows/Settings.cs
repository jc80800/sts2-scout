using System.IO;
using Scout.Core;

namespace Scout.Windows;

public sealed record Settings
{
    public string ProcessName { get; init; } = "SlayTheSpire2";
    public double OffsetX { get; init; } = 24;
    public double OffsetY { get; init; } = 80;
    public double Opacity { get; init; } = .9;
    public bool ClickThrough { get; init; } = true;
    public uint HotkeyModifiers { get; init; } = 6; // Control | Shift
    public uint HotkeyVirtualKey { get; init; } = 0x53; // S
    public bool DiagnosticCapture { get; init; }
    public int PollMilliseconds { get; init; } = 1000;
    public RunContext Context { get; init; } = new("unconfigured", [], [], [], []);
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProcessName) || ProcessName.IndexOfAny(['/', '\\', '.']) >= 0 || !double.IsFinite(OffsetX + OffsetY + Opacity) || Opacity is < .2 or > 1 || PollMilliseconds is < 500 or > 10000 || HotkeyModifiers > 15 || HotkeyVirtualKey is < 1 or > 254 || Context.Act is < 1 or > 4 || Context.Ascension is < 0 or > 30 || Context.Gold < 0 || Context.ReserveGold < 0) throw new InvalidDataException("Invalid settings; see docs/windows-guide.md");
    }
}
