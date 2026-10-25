using System.Diagnostics;
using System.Globalization;
using Scout.Core;
using Scout.Ocr;

// Developer-only CLI. Runtime never launches processes or writes image crops.
if (args.Length < 2) { Console.Error.WriteLine("Scout.Recognize <pack.json> <frame.pgm> [--cli] [expected,id,list]"); return 2; }
var pack = Json.Read<StrategyPack>(args[0]); PackValidation.Validate(pack);
using var native = args.Contains("--cli") ? null : new TesseractOcr(Path.Combine(AppContext.BaseDirectory, "tessdata"));
IOfflineOcr ocr = native is not null ? native : new CliOcr();
var observation = new CardRewardRecognizer(ocr, pack).Recognize(GrayFrame.ReadPgm(args[1]), DateTimeOffset.UtcNow);
var context = new RunContext(pack.GameVersion, [], [], [], []);
var stable = new StabilityFilter(); stable.Push(observation); stable.Push(observation);
var accepted = stable.Push(observation);
Console.WriteLine(Json.Write(new { Observation = observation, Recommendations = accepted == null ? [] : new RecommendationEngine(pack).Rank(accepted, context) }));
var expected = args.Skip(2).FirstOrDefault(a => a != "--cli");
return observation.Screen != Screen.CardReward || (expected != null && !observation.Choices.Select(c => c.EntityId).SequenceEqual(expected.Split(','))) ? 1 : 0;

sealed class CliOcr : IOfflineOcr
{
    public OcrLine[] Read(GrayFrame crop)
    {
        var filename = Path.Combine(Path.GetTempPath(), "scout-ocr-" + Guid.NewGuid().ToString("N") + ".pgm");
        try
        {
            File.WriteAllText(filename, crop.ToPgm());
            var start = new ProcessStartInfo("tesseract") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "stdin", "stdout", "--tessdata-dir", Path.Combine(AppContext.BaseDirectory, "tessdata"), "-l", "eng", "--psm", "11", "-c", "tessedit_create_tsv=1" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start developer Tesseract CLI");
            process.StandardInput.Write(crop.ToPgm()); process.StandardInput.Close();
            var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException(error.GetAwaiter().GetResult());
            var words = output.Split('\n').Skip(1).Select(l => l.Split('\t')).Where(c => c.Length >= 12 && c[0] == "5" && !string.IsNullOrWhiteSpace(c[11]));
            var lines = words.GroupBy(c => string.Join(':', c.Take(5))).Select(g => new OcrLine(string.Join(' ', g.Select(c => c[11])), g.Average(c => double.Parse(c[10], CultureInfo.InvariantCulture)) / 100, new(0, 0, 1, 1))).ToArray();
            if (Environment.GetEnvironmentVariable("SCOUT_OCR_TRACE") == "1") Console.Error.WriteLine(Json.Write(lines));
            return lines;
        }
        finally { File.Delete(filename); }
    }
}
