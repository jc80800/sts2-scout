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
    private readonly Settings settings;
    private readonly StrategyPack pack;
    private readonly ScoutDatabase database;
    private readonly TemplateRecognizer? recognizer;
    private readonly RecommendationEngine engine;
    private readonly StabilityFilter stability = new();
    private readonly DecisionTracker decisions = new();
    private readonly DispatcherTimer timer;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 14 };
    private readonly StackPanel results = new();
    private readonly Button interaction = new() { Content = "Return to click-through", Margin = new Thickness(0, 8, 0, 0) };
    private nint hwnd;
    private bool interactive, busy, closed, affinity;
    private int lastProcessId;
    private GrayFrame? latest;
    private DateTimeOffset lastFrameAt;
    public OverlayWindow(ScoutPaths paths)
    {
        this.paths = paths;
        var settingsFile = paths.FilePath("settings.json");
        settings = File.Exists(settingsFile) ? Json.Read<Settings>(settingsFile) : new Settings(); settings.Validate();
        if (!File.Exists(settingsFile)) paths.Write("settings.json", Json.Write(settings));
        var localPack = paths.FilePath("strategy-pack.json");
        pack = Json.Read<StrategyPack>(File.Exists(localPack) ? localPack : Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json")); PackValidation.Validate(pack);
        var calibration = paths.FilePath("calibration.json");
        if (File.Exists(calibration)) recognizer = new(Json.Read<Calibration>(calibration), pack);
        engine = new(pack); database = new(paths);
        Width = 370; SizeToContent = SizeToContent.Height; MaxHeight = 720; Left = settings.OffsetX; Top = settings.OffsetY;
        Title = "Sts2Scout"; AllowsTransparency = true; Background = Brushes.Transparent; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowActivated = false; Opacity = settings.Opacity;
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "SCOUT  /  STS2", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = Brushes.Turquoise });
        panel.Children.Add(status); panel.Children.Add(results); panel.Children.Add(interaction);
        var capture = new Button { Content = "Save diagnostic frame (opt-in required)", Margin = new Thickness(0, 8, 0, 0) };
        capture.Click += (_, _) => SaveDiagnostic(); panel.Children.Add(capture);
        var legal = new TextBlock { Text = "AGPL-3.0 • No warranty • See bundled LICENSE\nSingle-player only • Local heuristics", Foreground = Brushes.LightGray, FontSize = 11, Margin = new Thickness(0, 12, 0, 0) }; panel.Children.Add(legal);
        var quit = new Button { Content = "Quit Scout", Margin = new Thickness(0, 8, 0, 0) }; quit.Click += (_, _) => Close(); panel.Children.Add(quit);
        Content = new Border { Background = new SolidColorBrush(Color.FromArgb(240, 17, 24, 39)), BorderBrush = Brushes.SlateGray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Child = panel };
        interaction.Click += (_, _) => SetInteractive(false);
        SourceInitialized += (_, _) => InitializeNative();
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(settings.PollMilliseconds) }; timer.Tick += async (_, _) => await Tick();
        Closed += (_, _) => { closed = true; timer.Stop(); Native.UnregisterHotKey(hwnd, 1); database.Dispose(); };
        status.Text = "Waiting for STS2. Use your configured interaction hotkey (default Ctrl+Shift+S).";
    }
    private void InitializeNative()
    {
        hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessage);
        affinity = Native.SetWindowDisplayAffinity(hwnd, 0x11);
        var hotkey = Native.RegisterHotKey(hwnd, 1, settings.HotkeyModifiers | 0x4000, settings.HotkeyVirtualKey);
        SetInteractive(!settings.ClickThrough || !hotkey);
        if (!hotkey) MessageBox.Show("Scout's hotkey is already in use. Interaction stays enabled. Change hotkeyModifiers/hotkeyVirtualKey in settings.json and restart.", "Scout");
        if (!affinity) { status.Text = "Capture disabled: Windows could not exclude Scout from screen capture."; paths.Log("Display affinity failed; capture disabled"); }
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
        if (busy || closed || !affinity) return;
        busy = true;
        try
        {
            var game = Native.FindGame(settings.ProcessName);
            if (game.ProcessId != lastProcessId) { stability.Reset(); decisions.Reset(); latest = null; lastProcessId = game.ProcessId; }
            if (game.Handle == 0) { status.Text = "Waiting for STS2. Launch through Steam; check processName in settings if needed."; results.Children.Clear(); stability.Reset(); decisions.Reset(); return; }
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
            if (frame == null) { status.Text = "Capture unavailable; use windowed/borderless mode and focus STS2."; results.Children.Clear(); stability.Reset(); decisions.Reset(); return; }
            latest = frame; lastFrameAt = DateTimeOffset.UtcNow;
            if (recognizer == null) { status.Text = "Calibration required. Capture a diagnostic frame, then follow docs/calibration.md. No live recognition is claimed."; return; }
            var observation = await Task.Run(() => recognizer.Recognize(frame, DateTimeOffset.UtcNow));
            if (closed) return;
            if (observation.Screen == Screen.Unknown) { status.Text = "Unknown screen / insufficient confidence"; results.Children.Clear(); decisions.Reset(); }
            var stable = stability.Push(observation);
            if (stable == null)
            {
                if (!stability.IsStable) { results.Children.Clear(); if (observation.Screen != Screen.Unknown) status.Text = "Checking consecutive frames…"; }
                return;
            }
            var selections = decisions.Observe(stable);
            database.Save(stable, pack, settings.Context, selections);
            var ranked = engine.Rank(stable, settings.Context);
            status.Text = $"{stable.Screen} • match {stable.Confidence:P0}\n" + (ranked.Length == 0 ? "No recommendation: unknown offers, prices, or mismatched/unconfigured strategy data." : "Heuristic ranking; skipping/saving gold remains an option.");
            results.Children.Clear();
            foreach (var r in ranked.Take(6)) results.Children.Add(new TextBlock { Text = $"\n#{r.Slot + 1} {pack.Entities.Single(e => e.Id == r.EntityId).Name}  {r.Score:0.##}\n" + string.Join("\n", r.Reasons), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontSize = 12 });
        }
        catch (Exception ex) { if (!closed) { status.Text = $"Scout paused this frame: {ex.Message}"; results.Children.Clear(); stability.Reset(); decisions.Reset(); paths.Log(ex.ToString()); } }
        finally { busy = false; }
    }
}
