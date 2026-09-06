using System.Diagnostics;
using System.Runtime.InteropServices;
using Scout.Core;

namespace Scout.Windows;

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant;
    }
    [DllImport("user32.dll")] internal static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(nint hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowDisplayAffinity(nint hwnd, uint affinity);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sx, int sy, uint operation);

    internal static bool TryExcludeWindowFromCapture(nint hwnd, out int error)
    {
        const uint WdaExcludeFromCapture = 0x11;
        var excluded = SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
        error = excluded ? 0 : Marshal.GetLastWin32Error();
        return excluded;
    }

    internal static (nint Handle, int ProcessId) FindGame(string name)
    {
        foreach (var process in Process.GetProcessesByName(name))
        {
            using (process)
            {
                try { var hwnd = process.MainWindowHandle; if (hwnd != 0 && IsWindowVisible(hwnd) && !IsIconic(hwnd)) return (hwnd, process.Id); }
                catch (InvalidOperationException) { }
            }
        }
        return (0, 0);
    }
    internal static GrayFrame? Capture(nint hwnd, int processId)
    {
        // Capture only the foreground client's rectangle. Display affinity normally excludes Scout,
        // but callers deliberately continue with potentially contaminated frames if affinity is unavailable.
        // No rendering hooks, process handles, memory access, game files, or PrintWindow messages.
        if (hwnd == 0 || GetForegroundWindow() != hwnd || IsIconic(hwnd) || !GetClientRect(hwnd, out var r)) return null;
        GetWindowThreadProcessId(hwnd, out var actualId);
        if (actualId != processId) return null;
        var width = r.Right - r.Left; var height = r.Bottom - r.Top;
        if (width < 64 || height < 64 || (long)width * height > 16_000_000) return null;
        var origin = new Point(); if (!ClientToScreen(hwnd, ref origin)) return null;
        var dc = GetDC(0); if (dc == 0) return null;
        var memory = CreateCompatibleDC(dc); nint bitmap = 0, old = 0;
        try
        {
            if (memory == 0) return null;
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf<BitmapInfo>(), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0); if (bitmap == 0) return null;
            old = SelectObject(memory, bitmap);
            if (!BitBlt(memory, 0, 0, width, height, dc, origin.X, origin.Y, 0x00CC0020) || GetForegroundWindow() != hwnd) return null;
            var data = new byte[width * height * 4]; Marshal.Copy(bits, data, 0, data.Length);
            var gray = new byte[width * height];
            for (var i = 0; i < gray.Length; i++) gray[i] = (byte)((data[i * 4 + 2] * 77 + data[i * 4 + 1] * 150 + data[i * 4] * 29) >> 8);
            return new(width, height, gray);
        }
        finally { if (old != 0) SelectObject(memory, old); if (bitmap != 0) DeleteObject(bitmap); if (memory != 0) DeleteDC(memory); ReleaseDC(0, dc); }
    }
}
