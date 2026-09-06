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
    private readonly Button interaction = new() { Content = "Return to click-through", Margin = new Thickness(0, 8, 0, 0) };
    private nint hwnd;
    private bool interactive, busy, closed;
    private int lastProcessId;
    private GrayFrame? latest;
    private DateTimeOffset lastFrameAt;
    public OverlayWindow(ScoutPaths paths)
    {
        this.paths = paths;
        var settingsFile = paths.FilePath("settings.json");
        settings = File.Exists(settingsFile) ? Json.Read<Settings>(settingsFile) : new Settings(); settings.Validate();
        if (!File.Exists(settingsFile)) paths.Write("settings.json", Json.Write(settings));
        var loaded = PackCache.Load(paths, Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"), settings.Context.GameVersion);
        pack = loaded.Pack; dataStatus = loaded.Status;
        settings = settings with { Context = Deck.Migrate(settings.Context) };
        var calibration = paths.FilePath("calibration.json");
        if (File.Exists(calibration)) { profile = Json.Read<Calibration>(calibration); ConfigureRecognizer(); }
        engine = new(pack); database = new(paths);
        Width = 370; SizeToContent = SizeToContent.Height; MaxHeight = 720; Left = settings.OffsetX; Top = settings.OffsetY;
        // Display affinity is unreliable for the layered HWND created by AllowsTransparency.
        // An opaque, composition-managed HWND trades transparent corner pixels for reliable exclusion.
        Title = "Sts2Scout"; AllowsTransparency = false; Background = new SolidColorBrush(Color.FromRgb(17, 24, 39)); WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowActivated = false; Opacity = settings.Opacity;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "SCOUT  /  STS2", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brushes.Turquoise });
        panel.Children.Add(status); panel.Children.Add(captureWarning);
        var edit = new Button { Content = "Review / edit current deck" }; edit.Click += (_, _) => EditDeck(); panel.Children.Add(edit);
        var correct = new Button { Content = "Correct reward names / upgrades" }; correct.Click += (_, _) => CorrectReward(); panel.Children.Add(correct);
        var resume = new Button { Content = "Resume capture after correction" }; resume.Click += (_, _) => { corrected = false; stability.Reset(); currentReward = null; results.Children.Clear(); }; panel.Children.Add(resume);
        panel.Children.Add(new ScrollViewer { Content = results, MaxHeight = 390, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); panel.Children.Add(interaction);
        var capture = new Button { Content = "Save diagnostic frame (opt-in required)", Margin = new Thickness(0, 8, 0, 0) };
        capture.Click += (_, _) => SaveDiagnostic(); panel.Children.Add(capture);
        var legal = new TextBlock { Text = "AGPL-3.0 • No warranty • See bundled LICENSE\nSingle-player only • Local heuristics", Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(legal);
        var quit = new Button { Content = "Quit Scout", Margin = new Thickness(0, 8, 0, 0) }; quit.Click += (_, _) => Close(); panel.Children.Add(quit);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 17, 24, 39)), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        interaction.Click += (_, _) => SetInteractive(false);
        SourceInitialized += (_, _) => InitializeNative();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.PollMilliseconds) }; timer.Tick += async (_, _) => await Tick();
        Closed += (_, _) => { closed = true; timer.Stop(); Native.UnregisterHotKey(hwnd, 1); database.Dispose(); };
        status.Text = "Waiting for STS2. Use your configured interaction hotkey (default Ctrl+Shift+S).";
    }
    private void ConfigureRecognizer()
    {
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
        else recognizer = new TemplateRecognizer(pack.Catalog == null ? profile : profile with { Probes = profile.Probes.Where(p => p.Screen != Screen.CardReward || p.Kind == "screen").ToArray() }, pack);
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
            if (currentReward == null) { status.Text = "Wait for a stable CardReward first."; return; }
            corrected = true;
            var editor = new RewardCorrection(pack, settings.Context, currentReward) { Owner = this };
            if (editor.ShowDialog() == true && editor.Corrected != null)
            {
                currentReward = editor.Corrected;
                database.Save(currentReward, pack, settings.Context, []); ShowReward(currentReward);
            }
            else corrected = false;
        }
        catch (Exception ex) { corrected = false; status.Text = "Could not save correction: " + ex.Message; paths.Log(ex.ToString()); }
        finally { dialogOpen = false; if (!closed) timer.Start(); }
    }
    private void ShowReward(Observation observation)
    {
        var ranked = engine.Rank(observation, settings.Context);
        status.Text = $"{observation.Screen} • match {observation.Confidence:P0}\n" +
            (observation.Screen == Screen.CardReward ? DeckRanking.BlockReason(pack, observation, settings.Context) ?? "Recommended: " + pack.Entities.Single(e => e.Id == ranked[0].EntityId).Name + (observation.Choices.Single(c => c.Slot == ranked[0].Slot).Upgraded ? "+" : "") : "Merchant heuristic ranking") +
            $"\nCatalog {pack.Catalog?.Version}\nStrategy {pack.PackVersion} • cached {pack.Catalog?.RetrievedAt:yyyy-MM-dd}\n{dataStatus}" + (corrected ? "\nUSER CONFIRMED • capture paused; resume before next reward" : "");
        results.Children.Clear();
        foreach (var choice in observation.Choices.OrderBy(c => c.Slot))
            results.Children.Add(new TextBlock { Text = $"Slot {choice.Slot + 1}: {pack.Entities.SingleOrDefault(e => e.Id == choice.EntityId)?.Name ?? "Unknown"}{(choice.Upgraded ? "+" : "")} • name match {choice.Confidence:P0}", Foreground = Brushes.Turquoise });
        foreach (var r in ranked)
        {
            var title = new TextBlock { Text = $"\n{(r == ranked[0] ? "Recommended" : "Alternative")}: {pack.Entities.Single(e => e.Id == r.EntityId).Name} • {r.Score:0.##}\n" + string.Join("\n", r.Reasons.Where(reason => !reason.Contains("baseline") && !reason.Contains("adding to") && !reason.Contains("Mechanic source")).OrderByDescending(reason => reason.Contains("trigger") || reason.Contains("payoff")).Take(4)), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            results.Children.Add(title);
            results.Children.Add(new Expander { Header = "Detailed score / sources", Foreground = Brushes.LightGray, Content = new TextBlock { Text = string.Join("\n", r.Reasons), TextWrapping = TextWrapping.Wrap } });
        }
        if (replay != null) results.Children.Add(new Expander { Header = "OCR diagnostics (raw text / rejected candidates)", Foreground = Brushes.LightGray, Content = new TextBox { Text = Json.Write(replay.Slots), IsReadOnly = true, TextWrapping = TextWrapping.Wrap } });
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
        interaction.Content = enabled ? "Return to click-through" : "Click-through • use interaction hotkey";
    }
    private void SaveDiagnostic()
    {
        if (!settings.DiagnosticCapture) { status.Text = "Set diagnosticCapture to true in settings.json, then restart. Screenshots are off by default."; return; }
        if (latest == null || DateTimeOffset.UtcNow - lastFrameAt > TimeSpan.FromSeconds(30)) { status.Text = "No recent game frame. Focus STS2 first, then use the hotkey and save within 30 seconds."; return; }
        var name = $"diagnostic-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmssfff}.pgm";
        paths.Write(name, latest.ToPgm()); status.Text = $"Saved {name} in Scout's data directory.";
    }
    private async Task Tick()
    {
        if (busy || closed || corrected) return;
        busy = true;
        try
        {
            var game = Native.FindGame(settings.ProcessName);
            if (game.ProcessId != lastProcessId) { stability.Reset(); decisions.Reset(); currentReward = null; latest = null; lastProcessId = game.ProcessId; }
            if (game.Handle == 0) { status.Text = "Waiting for STS2. Launch through Steam; check processName in settings if needed."; results.Children.Clear(); currentReward = null; stability.Reset(); decisions.Reset(); return; }
            var foreground = Native.GetForegroundWindow();
            if (foreground != game.Handle)
            {
                if (foreground != hwnd) { status.Text = "Paused: focus STS2 to capture."; results.Children.Clear(); stability.Reset(); decisions.Reset(); }
                return;
            }
            var origin = new Native.Point(); Native.ClientToScreen(game.Handle, ref origin);
            // WPF positions are DIPs; Win32 client origins are physical pixels.
            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var point = transform.Transform(new System.Windows.Point(origin.X, origin.Y));
            Left = point.X + settings.OffsetX; Top = point.Y + settings.OffsetY;
            var frame = await Task.Run(() => Native.Capture(game.Handle, game.ProcessId));
            if (closed) return;
            if (frame == null) { status.Text = "Capture unavailable; use windowed/borderless mode and focus STS2."; results.Children.Clear(); currentReward = null; stability.Reset(); decisions.Reset(); return; }
            latest = frame; lastFrameAt = DateTimeOffset.UtcNow;
            if (recognizer == null) { status.Text = "Calibration required. Capture a diagnostic frame, then follow docs/calibration.md. " + dataStatus; return; }
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
        finally { busy = false; }
    }
}
