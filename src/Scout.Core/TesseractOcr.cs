using System.Reflection;
using System.Runtime.InteropServices;

namespace Scout.Core;

/// <summary>Local native OCR only. Grayscale bytes remain in memory; no subprocess or network.</summary>
public sealed class TesseractOcr : INameOcr, IDisposable
{
    private nint handle;
    private readonly object gate = new();
    static TesseractOcr() => NativeLibrary.SetDllImportResolver(typeof(TesseractOcr).Assembly, Resolve);
    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? search)
    {
        if (name != "scout-tesseract") return 0;
        if (OperatingSystem.IsWindows())
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "x64");
            NativeLibrary.Load(Path.Combine(folder, "leptonica-1.82.0.dll"));
            return NativeLibrary.Load(Path.Combine(folder, "tesseract50.dll"));
        }
        return NativeLibrary.Load(OperatingSystem.IsMacOS() ? "/opt/homebrew/lib/libtesseract.dylib" : "libtesseract.so.5");
    }
    public TesseractOcr(string modelDirectory)
    {
        if (!File.Exists(Path.Combine(modelDirectory, "eng.traineddata"))) throw new InvalidDataException("Offline English OCR model missing");
        handle = TessBaseAPICreate();
        if (handle == 0) throw new InvalidOperationException("Could not create OCR engine");
        if (TessBaseAPIInit3(handle, modelDirectory, "eng") != 0) { Dispose(); throw new InvalidOperationException("Could not load English OCR model"); }
        TessBaseAPISetPageSegMode(handle, 7); // Single name line; no dictionary of card-specific templates.
    }
    public OcrReading[] Read(GrayFrame crop)
    {
        crop.Validate();
        if (crop.Width > 1500 || crop.Height > 400) throw new InvalidDataException("Name crop too large");
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle == 0, this);
            var readings = new List<OcrReading>();
            // Different ribbon shades need different thresholds. Disagreement is rejected upstream.
            foreach (var (threshold, scale) in new[] { (-1, 1), (-1, 2), (-1, 4) }.Concat(new[] { 110, 130, 150, 165, 175 }.SelectMany(t => new[] { (t, 2), (t, 4) })))
            {
                var prepared = Prepare(crop, threshold, scale);
                TessBaseAPISetImage(handle, prepared.Pixels, prepared.Width, prepared.Height, 1, prepared.Width);
                var pointer = TessBaseAPIGetUTF8Text(handle);
                try { readings.Add(new(Marshal.PtrToStringUTF8(pointer)?.Trim() ?? "", TessBaseAPIMeanTextConf(handle) / 100d, threshold < 0 ? $"grayscale-{scale}x" : $"isolated-light-ink-{threshold}-{scale}x")); }
                finally { if (pointer != 0) TessDeleteText(pointer); TessBaseAPIClear(handle); }
            }
            return readings.ToArray();
        }
    }
    public static GrayFrame Prepare(GrayFrame crop, int threshold, int scale = 4)
    {
        crop.Validate();
        if (scale is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(scale));
        const int border = 16;
        var w = crop.Width * scale + border * 2; var h = crop.Height * scale + border * 2;
        var pixels = Enumerable.Repeat((byte)255, w * h).ToArray();
        var ink = crop.Pixels.Select(v => v > threshold).ToArray();
        if (threshold >= 0)
        {
            // The ribbon background connects to the crop boundary; letter interiors are
            // enclosed by their dark outline. Remove only boundary-connected light pixels.
            var queue = new Queue<int>();
            void Visit(int x, int y)
            {
                var i = y * crop.Width + x;
                if (!ink[i]) return;
                ink[i] = false; queue.Enqueue(i);
            }
            for (var x = 0; x < crop.Width; x++) { Visit(x, 0); Visit(x, crop.Height - 1); }
            for (var y = 0; y < crop.Height; y++) { Visit(0, y); Visit(crop.Width - 1, y); }
            while (queue.TryDequeue(out var i))
            {
                var x = i % crop.Width; var y = i / crop.Width;
                if (x > 0) Visit(x - 1, y); if (x + 1 < crop.Width) Visit(x + 1, y);
                if (y > 0) Visit(x, y - 1); if (y + 1 < crop.Height) Visit(x, y + 1);
            }
            // Suppress detached ribbon specks/lines, retaining letter-sized components.
            var seen = new bool[ink.Length];
            for (var first = 0; first < ink.Length; first++)
            {
                if (!ink[first] || seen[first]) continue;
                var component = new List<int>(); var pending = new Queue<int>(); pending.Enqueue(first); seen[first] = true;
                while (pending.TryDequeue(out var pos))
                {
                    component.Add(pos); var cx = pos % crop.Width; var cy = pos / crop.Width;
                    foreach (var (dx, dy) in new[] { (0, 1), (0, -1), (1, 0), (-1, 0) })
                    {
                        var nx = cx + dx; var ny = cy + dy;
                        if (nx < 0 || ny < 0 || nx >= crop.Width || ny >= crop.Height) continue;
                        var next = ny * crop.Width + nx;
                        if (ink[next] && !seen[next]) { seen[next] = true; pending.Enqueue(next); }
                    }
                }
                var height = component.Max(i => i / crop.Width) - component.Min(i => i / crop.Width) + 1;
                var width = component.Max(i => i % crop.Width) - component.Min(i => i % crop.Width) + 1;
                // Dot accents remain; only wide flat strokes away from normal glyph shape go.
                if (width > height * 5 || component.Count <= 1) foreach (var pos in component) ink[pos] = false;
            }
        }
        for (var y = 0; y < crop.Height * scale; y++)
            for (var x = 0; x < crop.Width * scale; x++)
            {
                double Source(int sx, int sy)
                {
                    sx = Math.Clamp(sx, 0, crop.Width - 1); sy = Math.Clamp(sy, 0, crop.Height - 1);
                    return threshold < 0 ? crop.Pixels[sy * crop.Width + sx] : ink[sy * crop.Width + sx] ? 0 : 255;
                }
                var fx = (x + .5) / scale - .5; var fy = (y + .5) / scale - .5;
                var ix = (int)Math.Floor(fx); var iy = (int)Math.Floor(fy); var dx = fx - ix; var dy = fy - iy;
                var value = Source(ix, iy) * (1 - dx) * (1 - dy) + Source(ix + 1, iy) * dx * (1 - dy) + Source(ix, iy + 1) * (1 - dx) * dy + Source(ix + 1, iy + 1) * dx * dy;
                pixels[(y + border) * w + x + border] = (byte)Math.Clamp(Math.Round(value), 0, 255);
            }
        return new(w, h, pixels);
    }
    public void Dispose() { lock (gate) { if (handle != 0) { TessBaseAPIDelete(handle); handle = 0; } } }
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern nint TessBaseAPICreate();
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern int TessBaseAPIInit3(nint h, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, [MarshalAs(UnmanagedType.LPUTF8Str)] string language);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPISetPageSegMode(nint h, int mode);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPISetImage(nint h, byte[] image, int width, int height, int bytesPerPixel, int bytesPerLine);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern nint TessBaseAPIGetUTF8Text(nint h);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern int TessBaseAPIMeanTextConf(nint h);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern void TessDeleteText(nint text);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPIClear(nint h);
    [DllImport("scout-tesseract", CallingConvention = CallingConvention.Cdecl)] private static extern void TessBaseAPIDelete(nint h);
}
