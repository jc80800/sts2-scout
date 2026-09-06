namespace Scout.Core;

/// <summary>The only runtime write capability. Never accepts a game path.</summary>
public sealed class ScoutPaths
{
    public string Root { get; }
    public ScoutPaths(string localAppData)
    {
        Root = Path.GetFullPath(Path.Combine(localAppData, "Sts2Scout"));
        Directory.CreateDirectory(Root);
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Scout data directory must not be a symlink/junction");
    }
    public string FilePath(string name)
    {
        if ((File.GetAttributes(Root) & FileAttributes.ReparsePoint) != 0) throw new IOException("Scout root was replaced by a link");
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(['/', '\\', ':']) >= 0 || Path.GetFileName(name) != name) throw new ArgumentException("Only a Scout-owned filename is accepted");
        var path = Path.Combine(Root, name);
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Scout files must not be symlinks");
        return path;
    }
    public void Write(string name, string contents) => File.WriteAllText(FilePath(name), contents);
    public void AtomicWrite(string name, string contents)
    {
        var temporary = name + ".pending";
        File.WriteAllText(FilePath(temporary), contents);
        File.Move(FilePath(temporary), FilePath(name), true);
    }
    public void Log(string message)
    {
        var path = FilePath("scout.log");
        if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, FilePath("scout.previous.log"), true);
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {message}\n");
    }
}
