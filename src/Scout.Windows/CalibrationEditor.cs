using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Scout.Core;

namespace Scout.Windows;

public sealed class CalibrationEditor : Window
{
    public Calibration? Saved { get; private set; }
    public CalibrationEditor(GrayFrame frame, string gameVersion)
    {
        Title = "Scout — Set up scan"; Width = 1100; Height = 820; MinWidth = 720; MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
        var instructions = new TextBlock { Text = "Use a three-card reward with no card hovered. Select a step, then drag a box around that text in the image. Include the full name and +, but not the energy cost.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };
        DockPanel.SetDock(instructions, Dock.Top); root.Children.Add(instructions);
        var steps = new ComboBox { ItemsSource = new[] { "1. Reward heading", "2. Left card name", "3. Middle card name", "4. Right card name" }, SelectedIndex = 0 };
        DockPanel.SetDock(steps, Dock.Top); root.Children.Add(steps);
        var bottom = new StackPanel(); DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        var message = new TextBlock { Text = "Draw all four boxes. The heading must be unique to the reward screen.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 8) }; bottom.Children.Add(message);
        var save = new Button { Content = "Save scan setup", IsEnabled = false }; bottom.Children.Add(save);
        var canvas = new Canvas { Width = frame.Width, Height = frame.Height, Background = Brushes.Black, ClipToBounds = true };
        canvas.Children.Add(new Image { Width = frame.Width, Height = frame.Height, Source = BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Gray8, null, frame.Pixels, frame.Width) });
        root.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, Margin = new Thickness(0, 12, 0, 0) });
        var regions = new Region?[4];
        var boxes = new Rectangle?[4];
        Point? start = null;
        Rectangle? drawing = null;
        var active = 0;
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            active = steps.SelectedIndex;
            if (boxes[active] != null) canvas.Children.Remove(boxes[active]!);
            regions[active] = null; save.IsEnabled = false;
            start = e.GetPosition(canvas);
            drawing = new Rectangle { Stroke = active == 0 ? Brushes.Gold : Brushes.Cyan, StrokeThickness = Math.Max(2, frame.Width / 500d), IsHitTestVisible = false };
            boxes[active] = drawing; canvas.Children.Add(drawing); canvas.CaptureMouse();
        };
        void Draw(Point end)
        {
            if (start == null || drawing == null) return;
            end.X = Math.Clamp(end.X, 0, frame.Width); end.Y = Math.Clamp(end.Y, 0, frame.Height);
            Canvas.SetLeft(drawing, Math.Min(start.Value.X, end.X)); Canvas.SetTop(drawing, Math.Min(start.Value.Y, end.Y));
            drawing.Width = Math.Abs(end.X - start.Value.X); drawing.Height = Math.Abs(end.Y - start.Value.Y);
        }
        canvas.MouseMove += (_, e) => { if (start != null) Draw(e.GetPosition(canvas)); };
        canvas.MouseLeftButtonUp += (_, e) =>
        {
            if (start == null || drawing == null) return;
            Draw(e.GetPosition(canvas)); canvas.ReleaseMouseCapture(); start = null;
            if (drawing.Width < 8 || drawing.Height < 8) { message.Text = "That box is too small. Draw around the complete text."; return; }
            regions[active] = new(Canvas.GetLeft(drawing) / frame.Width, Canvas.GetTop(drawing) / frame.Height, drawing.Width / frame.Width, drawing.Height / frame.Height);
            steps.SelectedIndex = Math.Min(3, active + 1);
            save.IsEnabled = regions.All(r => r != null);
            message.Text = save.IsEnabled ? "All four regions selected. Save, then focus the game to test recognition." : $"{regions.Count(r => r != null)} of 4 regions selected.";
        };
        save.Click += (_, _) =>
        {
            try { Saved = RewardSetup.Create(frame, regions[0]!, regions.Skip(1).Select(r => r!).ToArray(), gameVersion); DialogResult = true; }
            catch (System.IO.InvalidDataException ex) { message.Text = ex.Message; }
        };
    }
}
