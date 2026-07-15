using Microsoft.Win32;
using OdInstaller.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace OdInstaller.Uninstaller;
internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            var ownPath = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine uninstaller path.");
            var installDirectory = Path.GetDirectoryName(ownPath) ?? throw new InvalidOperationException("Cannot determine install directory.");
            var manifest = JsonFiles.ReadInstalledManifest(Path.Combine(installDirectory, ".odinstaller-installed.json"));
            if (MessageBox.Show($"Do you want to remove {manifest.ApplicationId}?", "Uninstall", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return 0;
            var locked = new List<string>();
            foreach (var shortcut in manifest.Shortcuts) TryDelete(shortcut, locked);
            foreach (var relative in manifest.Files)
            {
                var path = Path.Combine(installDirectory, relative);
                if (!string.Equals(path, ownPath, StringComparison.OrdinalIgnoreCase)) TryDelete(path, locked);
            }
            TryDelete(Path.Combine(installDirectory, ".odinstaller-installed.json"), locked);
            foreach (var key in manifest.RegistryKeys) Registry.CurrentUser.DeleteSubKeyTree(key, false);
            foreach (var directory in manifest.Directories.OrderByDescending(x => x.Length)) { var path = Path.Combine(installDirectory, directory); if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any()) Directory.Delete(path); }
            if (locked.Count > 0) { MessageBox.Show("Some files are still in use:\n" + string.Join("\n", locked), "Uninstall", MessageBoxButton.OK, MessageBoxImage.Warning); return 1; }
            ScheduleSelfDelete(ownPath, installDirectory); MessageBox.Show($"{manifest.ApplicationId} has been removed.", "Uninstall", MessageBoxButton.OK, MessageBoxImage.Information); return 0;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstallation failed", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
    private static void TryDelete(string path, ICollection<string> locked) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { locked.Add(path); } catch (UnauthorizedAccessException) { locked.Add(path); } }
    // A detached cmd process waits briefly so Windows can release the running executable.
    private static void ScheduleSelfDelete(string ownPath, string installDirectory) => Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping 127.0.0.1 -n 3 > nul & del /f /q \"{ownPath}\" & rmdir \"{installDirectory}\"") { CreateNoWindow = true, UseShellExecute = false });
}
