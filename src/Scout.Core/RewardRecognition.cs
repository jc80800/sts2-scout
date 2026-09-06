using System.Text;
using System.Text.RegularExpressions;

namespace Scout.Core;

public sealed record OcrReading(string Text, double Confidence, string Method);
public interface INameOcr { OcrReading[] Read(GrayFrame crop); }
public sealed record NameCandidate(string EntityId, string Name, double Similarity);
public sealed record NameMatch(string RawText, string Normalized, string? EntityId, bool Upgraded, double Confidence, NameCandidate[] Candidates, string Reason);
public sealed record SlotDiagnostic(int Slot, OcrReading[] Readings, NameMatch Match);
public sealed record RewardReplay(int Width, int Height, string FrameHash, Observation Observation, SlotDiagnostic[] Slots);

public static class CardNames
{
    public static string Normalize(string text)
    {
        text = text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('0', 'o').Replace('1', 'l').Replace('|', 'l');
        return string.Concat(text.Where(char.IsLetterOrDigit));
    }
    public static NameMatch Match(OcrReading reading, Entity[] pool)
    {
        var raw = reading.Text.Trim();
        var upgraded = Regex.IsMatch(raw, @"\+\s*[.,;:'""~\-]*$");
        var normalized = Normalize(raw.Replace("+", ""));
        var candidates = pool.Select(e => new NameCandidate(e.Id, e.Name, Similarity(normalized, Normalize(e.Name))))
            .OrderByDescending(c => c.Similarity).ThenBy(c => c.EntityId, StringComparer.Ordinal).Take(3).ToArray();
        var best = candidates.FirstOrDefault();
        string reason;
        var confidence = best == null ? 0 : Math.Min(best.Similarity, reading.Confidence);
        if (!double.IsFinite(reading.Confidence) || reading.Confidence < .60 || reading.Confidence > 1) reason = "OCR engine confidence below 0.60";
        else if (normalized.Length < 3 || best == null || best.Similarity < .90) reason = "Name similarity below 0.90; no safe catalog match";
        else if (candidates.Length > 1 && best.Similarity - candidates[1].Similarity < .08) reason = "Ambiguous: candidate margin below 0.08";
        else if (raw.Contains('+') && !upgraded) reason = "Upgrade punctuation is ambiguous";
        else return new(raw, normalized, best.EntityId, upgraded, best.Similarity, candidates, "Accepted: OCR >= 0.60, name >= 0.90, margin >= 0.08; scores are not probabilities");
        return new(raw, normalized, null, upgraded, confidence, candidates, reason);
    }
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        var prev = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var row = new int[b.Length + 1]; row[0] = i;
            for (var j = 1; j <= b.Length; j++) row[j] = Math.Min(Math.Min(row[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            prev = row;
        }
        return 1 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }
}

public sealed class RewardRecognizer : IScreenRecognizer
{
    private readonly TemplateRecognizer screens;
    private readonly INameOcr ocr;
    private readonly Region[] regions;
    private readonly Entity[] pool;
    public RewardRecognizer(Calibration calibration, StrategyPack pack, string character, INameOcr ocr)
    {
        this.ocr = ocr;
        // Ignore legacy reward entity templates, preserving all merchant and screen probes.
        screens = new(calibration with { Probes = calibration.Probes.Where(p => p.Screen != Screen.CardReward || p.Kind == "screen").ToArray() }, pack);
        regions = calibration.RewardNameRegions ?? [];
        if (regions.Length != 3 || regions.Any(r => !r.Valid) || regions.Distinct().Count() != 3) throw new InvalidDataException("Configure exactly three distinct rewardNameRegions in calibration");
        pool = CatalogValidation.Pool(pack, character);
    }
    public Observation Recognize(GrayFrame frame, DateTimeOffset at) => Replay(frame, at).Observation;
    public RewardReplay Replay(GrayFrame frame, DateTimeOffset at)
    {
        var detected = screens.Recognize(frame, at);
        if (detected.Screen != Screen.CardReward) return new(frame.Width, frame.Height, frame.Hash, detected, []);
        var slots = regions.Select((r, slot) =>
        {
            var width = Math.Max(1, (int)Math.Round(r.Width * frame.Width)); var height = Math.Max(1, (int)Math.Round(r.Height * frame.Height));
            var readings = ocr.Read(new(width, height, TemplateRecognizer.Sample(frame, r, width, height)));
            var matches = readings.Select(reading => CardNames.Match(reading, pool)).ToArray();
            var accepted = matches.Where(m => m.EntityId != null).ToArray();
            var best = accepted.OrderByDescending(m => m.Confidence).FirstOrDefault() ?? matches.OrderByDescending(m => m.Confidence).FirstOrDefault() ?? new("", "", null, false, 0, [], "OCR returned no text");
            if (accepted.Select(m => m.EntityId).Distinct().Count() > 1) best = best with { EntityId = null, Reason = "OCR preprocessing readings disagree on card identity; confirm manually" };
            // Seeing a trailing plus is positive upgrade evidence. Its omission by another
            // threshold is not evidence of a base card (thin plus strokes can disappear).
            if (best.EntityId != null && accepted.Any(m => m.Upgraded)) best = accepted.Where(m => m.Upgraded).OrderByDescending(m => m.Confidence).First();
            return new SlotDiagnostic(slot, readings, best);
        }).ToArray();
        return new(frame.Width, frame.Height, frame.Hash, detected with { Choices = slots.Select(s => new Choice(s.Slot, s.Match.EntityId, s.Match.EntityId == null ? 0 : s.Match.Confidence, Upgraded: s.Match.Upgraded)).ToArray() }, slots);
    }
}
