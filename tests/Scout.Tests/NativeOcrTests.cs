using System.IO.Compression;
using Scout.Core;
using Scout.Ocr;
using Xunit;
namespace Scout.Tests;

public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute() { if (!OperatingSystem.IsWindows()) Skip = "Native bundled Tesseract binaries require Windows; cross-platform CLI fixture verification is separate."; }
}
public sealed class NativeOcrTests
{
    [WindowsFact]
    public void BundledNativeOcrRecognizesSyntheticRewardWithoutTemplates()
    {
        var temporary = Path.GetTempFileName();
        try
        {
            using (var input = File.OpenRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","ocr-synthetic.pgm.gz")))
            using (var gzip = new GZipStream(input,CompressionMode.Decompress))
            using (var output = File.Create(temporary)) gzip.CopyTo(output);
            var pack=Json.Read<StrategyPack>(Path.Combine(AppContext.BaseDirectory,"data","strategy-pack.json"));
            using var ocr=new TesseractOcr(Path.Combine(AppContext.BaseDirectory,"tessdata"));
            var result=new CardRewardRecognizer(ocr,pack).Recognize(GrayFrame.ReadPgm(temporary),DateTimeOffset.UtcNow);
            Assert.Equal(Screen.CardReward,result.Screen);
            Assert.Equal(["card:pommel_strike","card:breakthrough","card:juggling"],result.Choices.Select(c=>c.EntityId));
        }
        finally { File.Delete(temporary); }
    }
}
