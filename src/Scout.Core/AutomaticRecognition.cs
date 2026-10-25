using System.Globalization;
using System.Text;

namespace Scout.Core;

public sealed record NameMatch(string EntityId, double Confidence, bool Upgraded);
public sealed class CardNameMatcher
{
    private readonly (Entity Entity, string Name)[] names;
    public CardNameMatcher(StrategyPack pack)
    {
        names = pack.Entities.Where(e => e.Kind == EntityKind.Card)
            .SelectMany(e => new[] { e.Name }.Concat(e.Facts?.Aliases ?? []).Select(n => (e, Normalize(n)))).ToArray();
    }
    public static string Normalize(string text) => new(text.Normalize(NormalizationForm.FormKD).ToLowerInvariant()
        .Where(c => char.IsLetterOrDigit(c) && CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    public NameMatch? Match(string text, double ocrConfidence, string? character = null)
    {
        if (!double.IsFinite(ocrConfidence) || ocrConfidence < .65 || ocrConfidence > 1) return null;
        var query = Normalize(text);
        if (query.Length < 3 || query.Length > 45) return null;
        var candidates = names.Where(n => character == null || n.Entity.Facts == null || n.Entity.Facts.Character == character || n.Entity.Facts.Character is "colorless" or "token" or "event")
            .Select(n => (n.Entity.Id, Distance: Distance(query, n.Name), Length: Math.Max(query.Length, n.Name.Length)))
            .GroupBy(n => n.Id).Select(g => g.OrderBy(n => n.Distance).First()).OrderBy(n => n.Distance).ThenBy(n => n.Id, StringComparer.Ordinal).ToArray();
        if (candidates.Length == 0) return null;
        var best = candidates[0];
        // Short names require exact OCR. Longer names tolerate one edit, exceptionally two.
        var allowed = query.Length >= 15 ? 2 : query.Length >= 7 ? 1 : 0;
        if (best.Distance > allowed || (candidates.Length > 1 && candidates[1].Distance - best.Distance < (best.Distance == 0 ? 1 : 2))) return null;
        var confidence = best.Distance == 0 ? .99 : 1 - (double)best.Distance / best.Length;
        if (confidence < .9 || (best.Distance > 0 && ocrConfidence < .8)) return null;
        return new(best.Id, Math.Min(confidence, .9 + .1 * ocrConfidence), text.Contains('+'));
    }
    internal static int Distance(string a, string b)
    {
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var previous = row[0]; row[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var old = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), previous + (a[i - 1] == b[j - 1] ? 0 : 1)); previous = old;
            }
        }
        return row[^1];
    }
}
public sealed record OcrLine(string Text, double Confidence, Region Bounds);
public interface IOfflineOcr { OcrLine[] Read(GrayFrame crop); }

/// <summary>English, centered three-choice rewards. No learned card images or calibration file.</summary>
public sealed class CardRewardRecognizer(IOfflineOcr ocr, StrategyPack pack, string? character = null) : IScreenRecognizer
{
    private readonly CardNameMatcher matcher = new(pack);
    public const string Profile = "reward-ocr-en-v1";
    public Observation Recognize(GrayFrame frame, DateTimeOffset at)
    {
        frame.Validate();
        Observation Unknown() => new(at, Screen.Unknown, 0, [], Profile, frame.Hash);
        if (frame.Width < 640 || frame.Height < 360 || frame.Pixels.Max() - frame.Pixels.Min() < 30) return Unknown();
        // Fit a centered 16:9 viewport for 16:10/ultrawide letterboxing; native 16:9 needs no adjustment.
        var viewport = Viewport(frame);
        Region Map(Region r) => new(viewport.X + r.X * viewport.Width, viewport.Y + r.Y * viewport.Height, r.Width * viewport.Width, r.Height * viewport.Height);
        var header = ReadRegion(frame, Map(new(.32, .19, .36, .14)));
        var anchor = header.Any(l => l.Confidence >= .75 && CardNameMatcher.Distance(CardNameMatcher.Normalize(l.Text), "chooseacard") <= 1);
        if (!anchor) return Unknown();
        var skip = ReadRegion(frame, Map(new(.41, .79, .18, .12)));
        if (!skip.Any(l => l.Confidence >= .75 && CardNameMatcher.Normalize(l.Text) == "skip")) return Unknown();
        var choices = new List<Choice>();
        // Broad name bands admit title movement and UI scaling, while excluding card rules text.
        for (var slot = 0; slot < 3; slot++)
        {
            var region = Map(new(.269 + slot * .183, .424, .104, .033));
            var matches = ReadRegion(frame, region).Select(l => matcher.Match(l.Text, l.Confidence, character)).Where(m => m != null).Cast<NameMatch>().ToArray();
            var distinct = matches.Select(m => (m.EntityId, m.Upgraded)).Distinct().ToArray();
            if (distinct.Length != 1) return Unknown();
            var best = matches.OrderByDescending(m => m.Confidence).First();
            choices.Add(new(slot, best.EntityId, best.Confidence, Upgraded: best.Upgraded));
        }
        return new(at, Screen.CardReward, choices.Min(c => c.Confidence), choices.ToArray(), Profile, frame.Hash);
    }
    private OcrLine[] ReadRegion(GrayFrame frame, Region region)
    {
        var crop = Crop(frame, region);
        var original = ocr.Read(crop);
        // First pass preserves antialiasing; second turns light title glyphs into dark-on-white text.
        return original.Concat(new[] { 195, 215 }.SelectMany(threshold =>
            ocr.Read(new(crop.Width, crop.Height, crop.Pixels.Select(p => (byte)(p >= threshold ? 0 : 255)).ToArray())))).ToArray();
    }
    public static Region Viewport(GrayFrame frame)
    {
        var aspect = (double)frame.Width / frame.Height;
        if (aspect > 16d / 9) { var width = 16d / 9 / aspect; return new((1 - width) / 2, 0, width, 1); }
        var height = aspect / (16d / 9); return new(0, (1 - height) / 2, 1, height);
    }
    public static GrayFrame Crop(GrayFrame frame, Region region)
    {
        var width = Math.Clamp((int)(region.Width * frame.Width * 2), 200, 1600);
        var height = Math.Max(32, (int)(width * region.Height * frame.Height / (region.Width * frame.Width)));
        frame.Validate();
        if (!region.Valid) throw new InvalidDataException("Invalid OCR crop");
        var pixels = new byte[width * height];
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var sx = Math.Clamp((region.X + (x + .5) / width * region.Width) * frame.Width - .5, 0, frame.Width - 1);
            var sy = Math.Clamp((region.Y + (y + .5) / height * region.Height) * frame.Height - .5, 0, frame.Height - 1);
            var x0 = (int)sx; var y0 = (int)sy; var x1 = Math.Min(x0 + 1, frame.Width - 1); var y1 = Math.Min(y0 + 1, frame.Height - 1);
            var dx = sx - x0; var dy = sy - y0;
            pixels[y * width + x] = (byte)Math.Round((1 - dy) * ((1 - dx) * frame.Pixels[y0 * frame.Width + x0] + dx * frame.Pixels[y0 * frame.Width + x1]) + dy * ((1 - dx) * frame.Pixels[y1 * frame.Width + x0] + dx * frame.Pixels[y1 * frame.Width + x1]));
        }
        return new(width, height, pixels);
    }
}
