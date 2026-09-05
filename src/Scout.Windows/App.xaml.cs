using System.IO;
using System.Windows;
using Scout.Core;

namespace Scout.Windows;

public partial class App : Application
{
    private Mutex? mutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        mutex = new Mutex(true, "Local\\Sts2Scout", out var created);
        if (!created) { MessageBox.Show("Scout is already running."); Shutdown(); return; }
        try
        {
            var paths = new ScoutPaths(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            MainWindow = new OverlayWindow(paths); MainWindow.Show();
        }
        catch (Exception ex) { MessageBox.Show($"Scout could not start: {ex.Message}\nCheck %LOCALAPPDATA%\\Sts2Scout. No game files were changed.", "Sts2Scout"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { mutex?.Dispose(); base.OnExit(e); }
}
