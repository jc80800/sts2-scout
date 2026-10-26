using Scout.Core;
using Xunit;

namespace Scout.Tests;

public sealed class SetupTests
{
    [Fact]
    public void LegacyRewardCalibrationGetsOcrRegionsWithoutKeepingFiveCardTemplates()
    {
        var screen = new Probe("screen", Screen.CardReward, 0, new(0, 0, .1, .1), [new("reward", 2, 2, [0, 100, 200, 255])]);
        var old = new Calibration(1, "old", "v0.107.1", 16d / 9, .96, .04,
            [screen, new("entity", Screen.CardReward, 0, new(0, 0, .1, .1), [new("card.bully", 2, 2, [0, 100, 200, 255])])]);
        var migrated = RewardSetup.Upgrade(old);
        Assert.Equal("0.107.1", migrated.GameVersion);
        Assert.Equal(screen, Assert.Single(migrated.Probes));
        Assert.Equal(3, migrated.RewardNameRegions!.Length);
        Assert.All(migrated.RewardNameRegions, r => Assert.True(r.Valid));
        Assert.Equal(Json.Write(migrated), Json.Write(RewardSetup.Upgrade(migrated)));
        Assert.Null(RewardSetup.Upgrade(old with { AspectRatio = 4d / 3 }).RewardNameRegions);
    }

    [Fact]
    public void SetupSamplesSelectedHeadingAndRecognizesThatFrame()
    {
        var frame = new GrayFrame(192, 108, Enumerable.Range(0, 192 * 108).Select(i => (byte)(i % 256)).ToArray());
        var pack = RewardSetup.LoadBundled(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
        var profile = RewardSetup.Create(frame, new(.4, .2, .2, .1), RewardSetup.DefaultNames(), "v0.107.1");
        Assert.Equal(Screen.CardReward, new TemplateRecognizer(profile, pack).Recognize(frame, DateTimeOffset.UtcNow).Screen);
        Assert.Throws<InvalidDataException>(() => RewardSetup.Create(new(192, 108, new byte[192 * 108]), new(.4, .2, .2, .1), RewardSetup.DefaultNames(), "0.107.1"));
        Assert.Throws<InvalidDataException>(() => RewardSetup.Create(frame, new(.4, .2, .2, .1), [new(0, 0, 1, 1)], "0.107.1"));
        Assert.NotEqual("0.107.1", GameVersions.Normalize("v0.110.1"));
    }

    [Fact]
    public void BundledCatalogIncludesStampedeInIroncladPool()
    {
        var pack = RewardSetup.LoadBundled(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
        Assert.Equal(577, pack.Entities.Length);
        Assert.Contains(CatalogValidation.Pool(pack, "ironclad"), e => e.Name == "Stampede");
    }
}
