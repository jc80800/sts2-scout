using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Scout.Core;
using Scout.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown,
            Resources = new ResourceDictionary { Source = new Uri("/Sts2Scout;component/Theme.xaml", UriKind.Relative) }
        };
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        watchdog.Tick += (_, _) => { Console.Error.WriteLine("UI smoke timed out"); Environment.Exit(1); }; watchdog.Start();
        var output = Path.GetFullPath("artifacts/ui-smoke"); Directory.CreateDirectory(output);
        try
        {
            var pack = RewardSetup.LoadBundled(Path.Combine(AppContext.BaseDirectory, "data", "strategy-pack.json"));
            var context = new RunContext("0.107.1", [], [], [], [], Character: "ironclad", Cards: [new("sts2.bash", 1, false)]);
            var deck = new DeckEditor(pack, context);
            Render(deck, output, "deck");
            // The all-card search must include Stampede, even before any OCR setup exists.
            var search = Descendants<TextBox>(deck).Single(t => t.Text == "");
            search.Text = "Stampede";
            var found = Descendants<ListBox>(deck).First();
            Require(found.Items.Count == 1, "Stampede missing from full catalog search");
            found.SelectedIndex = 0;
            FindButton(deck, "Add card").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            deck.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => FindButton(deck, "Save deck").RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Require(deck.ShowDialog() == true && deck.Saved!.Cards!.Any(c => c.EntityId == "sts2.stampede"), "Add/save deck failed");

            var observation = new Observation(DateTimeOffset.UtcNow, Screen.CardReward, 1,
                Enumerable.Range(0, 3).Select(i => new Choice(i, null, 0)).ToArray(), "manual", "manual");
            var correction = new RewardCorrection(pack, context, observation);
            Render(correction, output, "enter-cards");
            var boxes = Descendants<ComboBox>(correction).ToArray();
            foreach (var (box, name) in boxes.Zip(new[] { "Stampede", "Bully", "Pommel Strike" }))
                box.SelectedItem = box.Items.Cast<Entity>().Single(e => e.Name == name);
            correction.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => FindButton(correction, "Get advice").RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
            Require(correction.ShowDialog() == true && correction.Corrected!.Choices.All(c => c.EntityId != null), "Manual reward entry failed");
            Require(DeckRanking.Rank(pack, correction.Corrected!, context).Length == 3, "Manual advice requires OCR unexpectedly");

            var frame = new GrayFrame(1920, 1080, Enumerable.Range(0, 1920 * 1080).Select(i => (byte)((i / 1920 / 32 + i % 1920 / 32) % 2 == 0 ? 50 : 110)).ToArray());
            var calibration = new CalibrationEditor(frame, "0.107.1"); Render(calibration, output, "scan-setup"); calibration.Close();
            var paths = new ScoutPaths(Path.Combine(Path.GetTempPath(), "scout-ui-" + Guid.NewGuid().ToString("N")));
            // A legacy partial local catalog must not replace the bundled catalog.
            paths.Write("strategy-pack.json", "{}");
            var main = new OverlayWindow(paths); Render(main, output, "main"); main.Close();
            Require(Json.Read<Settings>(paths.FilePath("settings.json")).SetupVersion == 2, "Fresh setup did not initialize");
            Console.WriteLine("PASS: fresh startup, full catalog search, add/save deck, manual advice, and WPF layout renders");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { app.Shutdown(); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static Button FindButton(DependencyObject root, string label) => Descendants<Button>(root).Single(b => Equals(b.Content, label));
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Render(Window window, string output, string name)
    {
        window.Show();
        window.Measure(new Size(window.Width, window.Height)); window.Arrange(new Rect(0, 0, window.Width, window.Height)); window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.Width, (int)window.Height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file); window.Hide();
    }
}
