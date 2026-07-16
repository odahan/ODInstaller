using Microsoft.Win32;
using OdInstaller.Core;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace OdInstaller.Uninstaller;

/// <summary>
/// Provides the entry point and core logic for removing an installed application.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Runs the uninstallation workflow.
    /// </summary>
    /// <param name="args">Command-line arguments. The uninstaller currently does not use them.</param>
    /// <returns>
    /// Zero when the uninstallation succeeds or is canceled by the user;
    /// otherwise, one when the uninstallation fails or some files remain locked.
    /// </returns>
    private static int Main(string[] args)
    {
        try
        {
            // Determine the directory that contains the running uninstaller.
            var ownPath = Environment.ProcessPath
                ?? throw new InvalidOperationException(
                    "Cannot determine uninstaller path.");

            var installDirectory = Path.GetDirectoryName(ownPath)
                ?? throw new InvalidOperationException(
                    "Cannot determine install directory.");

            // Load the file, shortcut, directory, and registry information
            // recorded during the installation.
            var manifestPath = Path.Combine(
                installDirectory,
                ".odinstaller-installed.json");

            var manifest = JsonFiles.ReadInstalledManifest(manifestPath);

            // Ask the user to confirm the removal before changing the system.
            var confirmation = MessageBox.Show(
                $"Do you want to remove {manifest.ApplicationId}?",
                "Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmation != MessageBoxResult.Yes)
            {
                return 0;
            }

            var locked = new List<string>();

            // Remove shortcuts created for the application.
            foreach (var shortcut in manifest.Shortcuts)
            {
                TryDelete(shortcut, locked);
            }

            // Remove application files while preserving the running uninstaller.
            foreach (var relative in manifest.Files)
            {
                var path = Path.Combine(installDirectory, relative);

                if (!string.Equals(
                        path,
                        ownPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(path, locked);
                }
            }

            // Remove the installation manifest.
            TryDelete(manifestPath, locked);

            // Remove the application's uninstall entry from the current user's registry.
            foreach (var key in manifest.RegistryKeys)
            {
                Registry.CurrentUser.DeleteSubKeyTree(key, throwOnMissingSubKey: false);
            }

            // Remove empty directories, starting with the deepest directories.
            foreach (var relativeDirectory in manifest.Directories
                         .OrderByDescending(directory => directory.Length))
            {
                var directory = Path.Combine(installDirectory, relativeDirectory);

                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }

            // Report files that could not be removed because they are locked or inaccessible.
            if (locked.Count > 0)
            {
                MessageBox.Show(
                    "Some files are still in use:\n" + string.Join("\n", locked),
                    "Uninstall",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return 1;
            }

            // The uninstaller cannot delete itself while it is running.
            // Start a detached command that waits for the process to exit,
            // then deletes the executable and its installation directory.
            ScheduleSelfDelete(ownPath, installDirectory);

            MessageBox.Show(
                $"{manifest.ApplicationId} has been removed.",
                "Uninstall",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                ex.Message,
                "Uninstallation failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            return 1;
        }
    }

    /// <summary>
    /// Attempts to delete a file and records the path when deletion is not possible.
    /// </summary>
    /// <param name="path">Path of the file to delete.</param>
    /// <param name="locked">
    /// Collection that receives paths that could not be deleted.
    /// </param>
    private static void TryDelete(
        string path,
        ICollection<string> locked)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // The file may be in use by another process.
            locked.Add(path);
        }
        catch (UnauthorizedAccessException)
        {
            // The current user may not have permission to remove the file.
            locked.Add(path);
        }
    }

    /// <summary>
    /// Starts a detached command process that deletes the running uninstaller
    /// after Windows releases the executable.
    /// </summary>
    /// <param name="ownPath">Path of the running uninstaller.</param>
    /// <param name="installDirectory">Directory to remove after deleting the uninstaller.</param>
    private static void ScheduleSelfDelete(
        string ownPath,
        string installDirectory)
    {
        // The delay gives the current process time to exit before the executable
        // and its containing directory are removed.
        Process.Start(
            new ProcessStartInfo(
                "cmd.exe",
                $"/c ping 127.0.0.1 -n 3 > nul & " +
                $"del /f /q \"{ownPath}\" & " +
                $"rmdir \"{installDirectory}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
    }
}
