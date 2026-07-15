using System.IO;

namespace OdInstaller.Setup;

internal static class InstallerLog
{
    private static readonly object Sync = new();
    private static string? _path;

    public static void Start()
    {
        try
        {
            var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Setup path is unavailable.");
            _path = Path.ChangeExtension(executable, ".install.log");
            Info($"Setup started. Executable: {executable}");
        }
        catch { }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Error(Exception exception) => Write("ERROR", exception.ToString());

    private static void Write(string level, string message)
    {
        if (_path is null) return;
        try { lock (Sync) File.AppendAllText(_path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}"); }
        catch { }
    }
}
