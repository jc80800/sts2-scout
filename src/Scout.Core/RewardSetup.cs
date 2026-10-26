namespace Scout.Core;

public static class GameVersions
{
    public static string Normalize(string version) => version.Trim().TrimStart('v', 'V');
}

public static class RewardSetup
{
    public static Region[] DefaultNames() => new[] { 531d, 881d, 1231d }
        .Select(x => new Region(x / 1920, 460d / 1080, 171d / 1920, 28d / 1080)).ToArray();

    public static Calibration Upgrade(Calibration profile)
    {
        var names = profile.RewardNameRegions;
        if (names == null && Math.Abs(profile.AspectRatio / (16d / 9) - 1) <= .015) names = DefaultNames();
        return profile with
        {
            GameVersion = GameVersions.Normalize(profile.GameVersion),
            RewardNameRegions = names,
            Version = profile.Version + (profile.RewardNameRegions == null && names != null ? "/ocr-1" : ""),
            // Reward identities now come from OCR, never from the old five-card templates.
            Probes = profile.Probes.Where(p => p.Screen != Screen.CardReward || p.Kind == "screen").ToArray()
        };
    }

    public static Calibration Create(GrayFrame frame, Region title, Region[] names, string gameVersion)
    {
        frame.Validate();
        if (!title.Valid || names.Length != 3 || names.Any(r => !r.Valid) || names.Distinct().Count() != 3)
            throw new InvalidDataException("Draw the reward title and all three card names.");
        var pixels = TemplateRecognizer.Sample(frame, title, 32, 16);
        if (pixels.Max() - pixels.Min() < 20) throw new InvalidDataException("The title selection is blank. Draw a box around the reward heading.");
        return new(1, "reward-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss"), GameVersions.Normalize(gameVersion),
            (double)frame.Width / frame.Height, .96, .04,
            [new("screen", Screen.CardReward, 0, title, [new("reward", 32, 16, pixels)])], names);
    }

    public static StrategyPack LoadBundled(string path)
    {
        var pack = Json.Read<StrategyPack>(path);
        PackValidation.Validate(pack);
        if (pack.Catalog == null) throw new InvalidDataException("This app is missing its full card catalog. Extract the complete Windows download.");
        return pack;
    }
}
