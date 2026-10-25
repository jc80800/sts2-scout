using System.Text;
using Scout.Core;
using Tesseract;

namespace Scout.Ocr;

/// <summary>In-process offline OCR. One instance is owned by the serialized capture loop.</summary>
public sealed class TesseractOcr : IOfflineOcr, IDisposable
{
    private readonly TesseractEngine engine;
    public TesseractOcr(string dataDirectory)
    {
        engine = new TesseractEngine(dataDirectory, "eng", EngineMode.LstmOnly);
        engine.SetVariable("user_defined_dpi", "150");
    }
    public OcrLine[] Read(GrayFrame crop)
    {
        crop.Validate();
        var header = Encoding.ASCII.GetBytes($"P5\n{crop.Width} {crop.Height}\n255\n");
        using var image = Pix.LoadFromMemory(header.Concat(crop.Pixels).ToArray());
        using var page = engine.Process(image, PageSegMode.SparseText);
        using var iterator = page.GetIterator();
        var lines = new List<OcrLine>(); iterator.Begin();
        do
        {
            var text = iterator.GetText(PageIteratorLevel.TextLine);
            if (string.IsNullOrWhiteSpace(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var bounds)) continue;
            var confidence = iterator.GetConfidence(PageIteratorLevel.TextLine) / 100.0;
            lines.Add(new(text.Trim(), confidence, new((double)bounds.X1 / crop.Width, (double)bounds.Y1 / crop.Height, (double)bounds.Width / crop.Width, (double)bounds.Height / crop.Height)));
        } while (iterator.Next(PageIteratorLevel.TextLine));
        return lines.ToArray();
    }
    public void Dispose() => engine.Dispose();
}
