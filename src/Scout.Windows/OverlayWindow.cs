using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Scout.Core;
using Scout.Storage;

namespace Scout.Windows;

public sealed class OverlayWindow : Window
{
    private readonly ScoutPaths paths;
    private Settings settings;
    private readonly StrategyPack pack;
    private readonly ScoutDatabase database;
    private IScreenRecognizer? recognizer;
    private TesseractOcr? ocr;
    private Calibration? profile;
    private Observation? currentReward;
    private RewardReplay? replay;
    private bool corrected, dialogOpen;
    private string dataStatus = "";
    private readonly RecommendationEngine engine;
    private readonly StabilityFilter stability = new();
    private readonly DecisionTracker decisions = new();
    private readonly DispatcherTimer timer;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 14 };
    private readonly TextBlock captureWarning = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gold, FontSize = 12, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
    private readonly StackPanel results = new();
    private readonly Button interaction = new() { Content = "Enable click-through", Margin = new Thickness(0, 8, 0, 0) };
    private nint hwnd;
    private bool interactive, busy, closed;
    private int lastProcessId;
    private GrayFrame? latest;
    private DateTimeOffset lastFrameAt;
    public OverlayWindow(ScoutPaths paths)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        this.paths = paths;
        var settingsFile = paths.FilePath("settings.json");
        settings = File.Exists(settingsFile) ? Json.Read<Settings>(settingsFile) : new Settings(); settings.Validate();
        pack = RewardSetup.LoadBundled(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
        dataStatus = $"{pack.Entities.Length} cards • bundled catalog • game {pack.GameVersion}";
        // One-time fresh-run reset removes obsolete five-card IDs, not user calibration.
        settings = settings.SetupVersion < 2
            ? settings with { SetupVersion = 2, ClickThrough = false, Context = new("unconfigured", [], [], [], []) }
            : settings with { Context = Deck.Migrate(settings.Context) with { GameVersion = GameVersions.Normalize(settings.Context.GameVersion) } };
        paths.AtomicWrite("settings.json", Json.Write(settings));
        var calibration = paths.FilePath("calibration.json");
        if (File.Exists(calibration))
        {
            try
            {
                profile = RewardSetup.Upgrade(Json.Read<Calibration>(calibration));
                ConfigureRecognizer(); paths.AtomicWrite("calibration.json", Json.Write(profile));
            }
            catch (Exception ex) { recognizer = null; profile = null; dataStatus += " • Scan setup needs attention"; paths.Log(ex.ToString()); }
        }
        engine = new(pack); database = new(paths);
        Width = 500; Height = Math.Min(820, SystemParameters.WorkArea.Height - 40); MinWidth = 420; MinHeight = 560;
        Left = Math.Clamp(settings.OffsetX, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width);
        Top = Math.Clamp(settings.OffsetY, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - Height);
        Title = "Scout — Card advice"; AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(17, 27, 40));
        WindowStyle = WindowStyle.SingleBorderWindow; ResizeMode = ResizeMode.CanResize;
        Topmost = true; ShowActivated = true; Opacity = 1;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "SCOUT", FontSize = 28, FontWeight = FontWeights.Bold, Foreground = Brushes.Turquoise });
        panel.Children.Add(new TextBlock { Text = "Card advice for your current deck", Foreground = Brushes.LightGray, Margin = new Thickness(0, 2, 0, 18) });
        panel.Children.Add(new TextBlock { Text = dataStatus, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(status); panel.Children.Add(captureWarning);
        void Action(string label, RoutedEventHandler action)
        {
            var button = new Button { Content = label }; button.Click += action; panel.Children.Add(button);
        }
        Action("Edit deck", (_, _) => EditDeck());
        Action("Enter cards", (_, _) => CorrectReward());
        Action("Resume scan", (_, _) => { corrected = false; stability.Reset(); currentReward = null; results.Children.Clear(); status.Text = "Focus the game to scan the next reward."; });
        panel.Children.Add(results);
        var setup = new StackPanel();
        var configure = new Button { Content = "Set up scan" }; configure.Click += (_, _) => SetupScan(); setup.Children.Add(configure);
        setup.Children.Add(new TextBlock { Text = "Focus the game on a reward, then return here. Draw the title and three card names once. No source code needed.", Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        var diagnostics = new CheckBox { Content = "Allow saving diagnostic images", Foreground = Brushes.LightGray, IsChecked = settings.DiagnosticCapture };
        diagnostics.Click += (_, _) => { settings = settings with { DiagnosticCapture = diagnostics.IsChecked == true }; paths.AtomicWrite("settings.json", Json.Write(settings)); }; setup.Children.Add(diagnostics);
        var capture = new Button { Content = "Save image" }; capture.Click += (_, _) => SaveDiagnostic(); setup.Children.Add(capture);
        panel.Children.Add(new Expander { Header = "Scan setup", IsExpanded = recognizer == null, Foreground = Brushes.White, Margin = new Thickness(0, 16, 0, 8), Content = setup });
        panel.Children.Add(interaction);
        panel.Children.Add(new TextBlock { Text = "Move or resize Scout beside the game. Ctrl+Shift+S toggles click-through.\nSingle-player • Local heuristics • AGPL-3.0 • No warranty", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, FontSize = 12, Margin = new Thickness(0, 12, 0, 0) });
        Action("Quit", (_, _) => Close());
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        interaction.Click += (_, _) => SetInteractive(false);
        SourceInitialized += (_, _) => InitializeNative();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.PollMilliseconds) }; timer.Tick += async (_, _) => await Tick();
        Closed += (_, _) =>
        {
            closed = true; timer.Stop(); Native.UnregisterHotKey(hwnd, 1); database.Dispose();
            if (!busy) ocr?.Dispose();
            settings = settings with { OffsetX = Left, OffsetY = Top };
            try { paths.AtomicWrite("settings.json", Json.Write(settings)); } catch (IOException) { }
        };
        status.Text = "Start here: Edit deck, then Enter cards or Set up scan. Confirm your game patch in the deck editor.";
    }
    private void ConfigureRecognizer()
    {
        recognizer = null;
        dataStatus = $"{pack.Entities.Length} cards • bundled catalog • game {pack.GameVersion}";
        if (profile == null) return;
        if (profile.GameVersion != pack.GameVersion)
        {
            recognizer = null; dataStatus = "Strategy data is for a different game version than calibration"; return;
        }
        if (profile.RewardNameRegions != null && pack.Catalog != null)
        {
            ocr ??= new TesseractOcr(Path.Combine(AppContext.BaseDirectory, "data", "ocr"));
            recognizer = new RewardRecognizer(profile, pack, settings.Context.Character, ocr);
        }
        else dataStatus = "Select Set up scan to mark the card names for your layout.";
    }
    private async void SetupScan()
    {
        if (dialogOpen) return;
        dialogOpen = true; timer.Stop();
        while (busy && !closed) await Task.Delay(50);
        if (closed) return;
        try
        {
            if (GameVersions.Normalize(settings.Context.GameVersion) != pack.GameVersion)
            { status.Text = $"Set your actual game version in Edit deck first. This catalog supports {pack.GameVersion}."; return; }
            if (latest == null || DateTimeOffset.UtcNow - lastFrameAt > TimeSpan.FromSeconds(30))
            { status.Text = "Focus the game on a three-card reward for a few seconds, then return and select Set up scan. Resume scan first if paused."; return; }
            var editor = new CalibrationEditor(latest, pack.GameVersion) { Owner = this };
            if (editor.ShowDialog() == true && editor.Saved != null)
            {
                profile = editor.Saved; paths.AtomicWrite("calibration.json", Json.Write(profile));
                ConfigureRecognizer(); corrected = false; currentReward = null; replay = null; stability.Reset(); results.Children.Clear();
                status.Text = "Scan setup saved. Focus the game to read the reward.";
            }
        }
        catch (Exception ex) { status.Text = "Scan setup: " + ex.Message; paths.Log(ex.ToString()); }
        finally { dialogOpen = false; if (!closed) timer.Start(); }
    }
    private async void EditDeck()
    {
        if (dialogOpen) return;
        dialogOpen = true; timer.Stop();
        while (busy && !closed) await Task.Delay(50);
        if (closed) return;
        try
        {
            var editor = new DeckEditor(pack, settings.Context) { Owner = this };
            if (editor.ShowDialog() == true && editor.Saved != null)
            {
                settings = settings with { Context = editor.Saved }; paths.AtomicWrite("settings.json", Json.Write(settings));
                ConfigureRecognizer(); stability.Reset();
                currentReward = null; replay = null; corrected = false; results.Children.Clear(); status.Text = "Deck saved. Focus STS2 to read the next reward.";
            }
        }
        catch (Exception ex) { status.Text = "Could not save deck: " + ex.Message; paths.Log(ex.ToString()); }
        finally { dialogOpen = false; if (!closed) timer.Start(); }
    }
    private async void CorrectReward()
    {
        if (dialogOpen) return;
        dialogOpen = true; timer.Stop();
        while (busy && !closed) await Task.Delay(50);
        if (closed) return;
        try
        {
            if (settings.Context.Character == "unconfigured") { status.Text = "Choose your character in Edit deck first."; return; }
            var wasCorrected = corrected;
            corrected = true;
            var reward = currentReward ?? new Observation(DateTimeOffset.UtcNow, Screen.CardReward, 1,
                Enumerable.Range(0, 3).Select(i => new Choice(i, null, 0)).ToArray(), "manual", "manual");
            var editor = new RewardCorrection(pack, settings.Context, reward) { Owner = this };
            if (editor.ShowDialog() == true && editor.Corrected != null)
            {
                currentReward = editor.Corrected with { At = DateTimeOffset.UtcNow }; replay = null;
                database.Save(currentReward, pack, settings.Context, []); ShowReward(currentReward);
            }
            else corrected = wasCorrected;
        }
        catch (Exception ex) { corrected = false; status.Text = "Could not save correction: " + ex.Message; paths.Log(ex.ToString()); }
        finally { dialogOpen = false; if (!closed) timer.Start(); }
    }
    private void ShowReward(Observation observation)
    {
        var ranked = engine.Rank(observation, settings.Context);
        status.Text = (corrected ? "Cards entered manually\n" : $"Reward screen • match {observation.Confidence:P0} (screen only)\n") +
            (observation.Screen == Screen.CardReward ? DeckRanking.BlockReason(pack, observation, settings.Context) ?? "Recommended: " + pack.Entities.Single(e => e.Id == ranked[0].EntityId).Name + (observation.Choices.Single(c => c.Slot == ranked[0].Slot).Upgraded ? "+" : "") : "Merchant heuristic ranking") +
            $"\nCatalog {pack.Catalog?.Version}\nStrategy {pack.PackVersion} • cached {pack.Catalog?.RetrievedAt:yyyy-MM-dd}\n{dataStatus}" + (corrected ? "\nScan paused • Enter cards again or Resume scan for the next reward" : "");
        results.Children.Clear();
        foreach (var choice in observation.Choices.OrderBy(c => c.Slot))
            results.Children.Add(new TextBlock { Text = $"{choice.Slot + 1}. {pack.Entities.SingleOrDefault(e => e.Id == choice.EntityId)?.Name ?? "Unread — use Enter cards or Set up scan"}{(choice.Upgraded ? "+" : "")}", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Turquoise, Margin = new Thickness(0, 6, 0, 0) });
        foreach (var r in ranked)
        {
            var title = new TextBlock { Text = $"\n{(r == ranked[0] ? "Recommended" : "Alternative")}: {pack.Entities.Single(e => e.Id == r.EntityId).Name} • {r.Score:0.##}\n" + string.Join("\n", r.Reasons.Where(reason => !reason.Contains("baseline") && !reason.Contains("adding to") && !reason.Contains("Mechanic source")).OrderByDescending(reason => reason.Contains("trigger") || reason.Contains("payoff")).Take(4)), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            results.Children.Add(title);
            results.Children.Add(new Expander { Header = "Why this card?", Foreground = Brushes.LightGray, Content = new TextBlock { Text = string.Join("\n", r.Reasons), TextWrapping = TextWrapping.Wrap } });
        }
        if (replay != null) results.Children.Add(new Expander { Header = "Scan details", Foreground = Brushes.LightGray, Content = new TextBox { Text = Json.Write(replay.Slots), IsReadOnly = true, TextWrapping = TextWrapping.Wrap } });
    }
    private void InitializeNative()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
        var affinity = Native.TryExcludeWindowFromCapture(hwnd, out var affinityError);
        var hotkey = Native.RegisterHotKey(hwnd, 1, settings.HotkeyModifiers | 0x4000, settings.HotkeyVirtualKey);
        SetInteractive(!settings.ClickThrough || !hotkey);
        if (!hotkey) MessageBox.Show("Scout's hotkey is already in use. Interaction stays enabled. Change hotkeyModifiers/hotkeyVirtualKey in settings.json and restart.", "Scout");
        if (!affinity)
        {
            captureWarning.Text = "Warning: Scout could not exclude its overlay from screen capture.\nCapture is continuing in fallback mode; frames may contain Scout pixels where the overlay overlaps STS2.";
            captureWarning.Visibility = Visibility.Visible;
            paths.Log($"SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) failed. Win32Error={affinityError}; Hwnd=0x{hwnd:X}; AllowsTransparency={AllowsTransparency}; OS={Environment.OSVersion}. Capture is continuing in fallback mode and frames may contain overlay pixels.");
        }
        timer.Start();
    }
    private nint WindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0312 && wParam == 1) { SetInteractive(!interactive); handled = true; }
        return 0;
    }
    private void SetInteractive(bool enabled)
    {
        interactive = enabled;
        var style = Native.GetWindowLongPtr(hwnd, -20).ToInt64();
        Native.SetWindowLongPtr(hwnd, -20, new nint(enabled ? style & ~(0x20 | 0x08000000) : style | 0x20 | 0x08000000));
        interaction.Content = enabled ? "Enable click-through" : "Click-through on • Ctrl+Shift+S to interact";
    }
    private void SaveDiagnostic()
    {
        if (!settings.DiagnosticCapture) { status.Text = "Enable Allow saving diagnostic images in Scan setup first."; return; }
        if (latest == null || DateTimeOffset.UtcNow - lastFrameAt > TimeSpan.FromSeconds(30)) { status.Text = "No recent game frame. Focus STS2 first, then use the hotkey and save within 30 seconds."; return; }
        var name = $"diagnostic-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.pgm";
        paths.Write(name, latest.ToPgm()); status.Text = $"Saved {name} in Scout's data directory.";
    }
    private async Task Tick()
    {
        if (busy || closed || corrected || dialogOpen) return;
        busy = true;
        try
        {
            var game = Native.FindGame(settings.ProcessName);
            if (game.ProcessId != lastProcessId) { stability.Reset(); decisions.Reset(); currentReward = null; latest = null; lastProcessId = game.ProcessId; }
            if (game.Handle == 0) { status.Text = "Waiting for the game. You can Edit deck and Enter cards now."; results.Children.Clear(); currentReward = null; stability.Reset(); decisions.Reset(); return; }
            var foreground = Native.GetForegroundWindow();
            if (foreground != game.Handle)
            {
                if (foreground != hwnd) { if (currentReward == null) status.Text = "Scan paused: focus the game. You can also Enter cards manually."; stability.Reset(); decisions.Reset(); }
                return;
            }
            var frame = await Task.Run(() => Native.Capture(game.Handle, game.ProcessId));
            if (closed) return;
            if (frame == null) { status.Text = "Capture unavailable; use windowed/borderless mode and focus STS2."; results.Children.Clear(); currentReward = null; stability.Reset(); decisions.Reset(); return; }
            latest = frame; lastFrameAt = DateTimeOffset.UtcNow;
            if (recognizer == null) { status.Text = "Ready to set up scan. Return to Scout and select Set up scan, or use Enter cards for manual advice. " + dataStatus; return; }
            var observation = await Task.Run(() =>
            {
                if (recognizer is RewardRecognizer reward) { replay = reward.Replay(frame, DateTimeOffset.UtcNow); return replay.Observation; }
                return recognizer.Recognize(frame, DateTimeOffset.UtcNow);
            });
            if (closed) return;
            if (observation.Screen == Screen.Unknown) { currentReward = null; status.Text = "Unknown screen / insufficient confidence"; results.Children.Clear(); decisions.Reset(); }
            var stable = stability.Push(observation);
            if (stable == null)
            {
                if (!stability.IsStable) { results.Children.Clear(); if (observation.Screen != Screen.Unknown) status.Text = "Checking consecutive frames…"; }
                return;
            }
            var selections = decisions.Observe(stable);
            database.Save(stable, pack, settings.Context, selections);
            currentReward = stable.Screen == Screen.CardReward ? stable : null;
            ShowReward(stable);

        }
        catch (Exception ex) { if (!closed) { status.Text = $"Scout paused this frame: {ex.Message}"; results.Children.Clear(); stability.Reset(); decisions.Reset(); paths.Log(ex.ToString()); } }
        finally { busy = false; if (closed) ocr?.Dispose(); }
    }
}
