using System.Security.Cryptography;

namespace Scout.Core;

public sealed record GrayFrame(int Width, int Height, byte[] Pixels)
{
    public string Hash => Convert.ToHexString(SHA256.HashData(Pixels));
    public void Validate() { if (Width <= 0 || Height <= 0 || (long)Width * Height != Pixels.Length || Pixels.Length > 40_000_000) throw new InvalidDataException("Invalid frame"); }
    public static GrayFrame ReadPgm(string path)
    {
        // Calibration uses portable, plain P2 grayscale; no proprietary assets or codecs.
        var lines = File.ReadLines(path).Select(l => l.Split('#')[0]);
        var tokens = string.Join(" ", lines).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 4 || tokens[0] != "P2" || tokens[3] != "255") throw new InvalidDataException("Expected P2 PGM with max value 255");
        var frame = new GrayFrame(int.Parse(tokens[1]), int.Parse(tokens[2]), tokens.Skip(4).Select(byte.Parse).ToArray());
        frame.Validate(); return frame;
    }
    public string ToPgm() => $"P2\n{Width} {Height}\n255\n" + string.Join(' ', Pixels);
}
public sealed record Region(double X, double Y, double Width, double Height)
{
    public bool Valid => double.IsFinite(X + Y + Width + Height) && X >= 0 && Y >= 0 && Width > 0 && Height > 0 && X + Width <= 1.000001 && Y + Height <= 1.000001;
}
public sealed record VisualTemplate(string Label, int Width, int Height, byte[] Pixels);
public sealed record Probe(string Kind, Screen Screen, int Slot, Region Region, VisualTemplate[] Templates);
public sealed record Calibration(int SchemaVersion, string Version, string GameVersion, double AspectRatio, double Threshold, double Margin, Probe[] Probes);
public interface IScreenRecognizer { Observation Recognize(GrayFrame frame, DateTimeOffset at); }
public sealed class TemplateRecognizer : IScreenRecognizer
{
    private readonly Calibration calibration;
    private readonly HashSet<string> entities;
    public TemplateRecognizer(Calibration calibration, StrategyPack pack)
    {
        this.calibration = calibration;
        entities = pack.Entities.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        if (calibration.SchemaVersion != 1 || calibration.GameVersion != pack.GameVersion || !double.IsFinite(calibration.Threshold) || calibration.Threshold is < .9 or > 1 || !double.IsFinite(calibration.Margin) || calibration.Margin is < .01 or > 1 || !double.IsFinite(calibration.AspectRatio) || calibration.AspectRatio <= 0) throw new InvalidDataException("Invalid calibration/version");
        foreach (var p in calibration.Probes)
        {
            if (!p.Region.Valid || p.Kind is not ("screen" or "entity" or "price" or "upgrade" or "selection") || !Enum.IsDefined(p.Screen) || p.Screen == Screen.Unknown || p.Slot < 0 || p.Templates.Length == 0 || p.Templates.Select(t => t.Label).Distinct().Count() != p.Templates.Length) throw new InvalidDataException("Invalid probe");
            foreach (var t in p.Templates)
            {
                new GrayFrame(t.Width, t.Height, t.Pixels).Validate();
                if (t.Pixels.Max() - t.Pixels.Min() < 20) throw new InvalidDataException("Template lacks contrast");
                if (p.Kind is "entity" or "selection" && !entities.Contains(t.Label)) throw new InvalidDataException("Unknown template entity");
                if (p.Kind == "price" && (!int.TryParse(t.Label, out var price) || price < 0)) throw new InvalidDataException("Invalid price label");
                if (p.Kind == "upgrade" && t.Label != "upgraded") throw new InvalidDataException("Invalid upgrade label");
            }
        }
        if (calibration.Probes.GroupBy(p => (p.Kind, p.Screen, p.Slot)).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate probe");
    }
    public static byte[] Sample(GrayFrame frame, Region r, int width, int height)
    {
        frame.Validate();
        if (!r.Valid || width <= 0 || height <= 0 || (long)width * height > 40_000_000) throw new InvalidDataException("Invalid sample");
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var sx = Math.Clamp((int)((r.X + (x + .5) / width * r.Width) * frame.Width), 0, frame.Width - 1);
                var sy = Math.Clamp((int)((r.Y + (y + .5) / height * r.Height) * frame.Height), 0, frame.Height - 1);
                pixels[y * width + x] = frame.Pixels[sy * frame.Width + sx];
            }
        return pixels;
    }
    private (string? Label, double Confidence) Match(GrayFrame frame, Probe probe)
    {
        var scores = probe.Templates.Select(t =>
        {
            var sample = Sample(frame, probe.Region, t.Width, t.Height);
            var difference = sample.Zip(t.Pixels, (a, b) => Math.Abs(a - b)).Average() / 255;
            return (t.Label, Score: 1 - difference);
        }).OrderByDescending(t => t.Score).ToArray();
        var best = scores[0];
        return best.Score >= calibration.Threshold && (scores.Length == 1 || best.Score - scores[1].Score >= calibration.Margin) ? (best.Label, best.Score) : (null, best.Score);
    }
    public Observation Recognize(GrayFrame frame, DateTimeOffset at)
    {
        frame.Validate();
        Observation Unknown() => new(at, Screen.Unknown, 0, [], calibration.Version, frame.Hash);
        if (Math.Abs((double)frame.Width / frame.Height / calibration.AspectRatio - 1) > .015 || frame.Pixels.Max() - frame.Pixels.Min() < 20) return Unknown();
        var screens = calibration.Probes.Where(p => p.Kind == "screen").Select(p => (p.Screen, Result: Match(frame, p))).Where(s => s.Result.Label != null).OrderByDescending(s => s.Result.Confidence).ToArray();
        if (screens.Length != 1) return Unknown();
        var screen = screens[0];
        var choices = calibration.Probes.Where(p => p.Screen == screen.Screen && p.Kind == "entity").Select(p =>
        {
            var entity = Match(frame, p);
            var priceProbe = calibration.Probes.SingleOrDefault(q => q.Screen == screen.Screen && q.Kind == "price" && q.Slot == p.Slot);
            var price = priceProbe == null ? (Label: (string?)null, Confidence: 0d) : Match(frame, priceProbe);
            var upgrade = calibration.Probes.SingleOrDefault(q => q.Screen == screen.Screen && q.Kind == "upgrade" && q.Slot == p.Slot);
            return new Choice(p.Slot, entity.Label, entity.Label == null ? 0 : entity.Confidence, price.Label == null ? null : int.Parse(price.Label), price.Label == null ? 0 : price.Confidence, upgrade != null && Match(frame, upgrade).Label != null);
        }).ToArray();
        var signals = calibration.Probes.Where(p => p.Screen == screen.Screen && p.Kind == "selection")
            .Select(p => (Probe: p, Result: Match(frame, p)))
            .Where(p => p.Result.Label != null)
            .Select(p => new Selection(p.Probe.Slot, p.Result.Label!, p.Result.Confidence, "Calibrated visual selection indicator; unconfirmed, may be hover/animation"))
            .ToArray();
        return new(at, screen.Screen, screen.Result.Confidence, choices, calibration.Version, frame.Hash, signals);
    }
}
public sealed class StabilityFilter
{
    private string? key;
    private int count;
    public bool IsStable => count >= 3;
    public Observation? Push(Observation observation)
    {
        if (observation.Screen == Screen.Unknown || !double.IsFinite(observation.Confidence) || observation.Confidence is < .9 or > 1) { Reset(); return null; }
        var next = Json.Write(new { observation.Screen, observation.ProfileVersion, Choices = observation.Choices.OrderBy(c => c.Slot).Select(c => new { c.Slot, c.EntityId, c.Price, c.Upgraded }), Selections = observation.SelectionSignals?.Select(s => new { s.Slot, s.EntityId }) });
        if (next != key) { key = next; count = 1; } else count++;
        return count == 3 ? observation : null;
    }
    public void Reset() { key = null; count = 0; }
}
