using System.IO;
using OdInstaller.Core;

namespace OdInstaller.Setup;

/// <summary>
/// Creates Windows shortcuts for the installed application.
/// </summary>
/// <remarks>
/// Shortcuts are stored as <c>.url</c> files. This avoids an extended COM
/// dependency while remaining natively supported by Windows.
/// </remarks>
internal static class ShortcutWriter
{
    /// <summary>
    /// Creates the requested Start menu and desktop shortcuts.
    /// </summary>
    /// <param name="manifest">
    /// Manifest containing the application name and executable name.
    /// </param>
    /// <param name="installDirectory">
    /// Installation directory containing the executable and, optionally,
    /// the <c>application.ico</c> file.
    /// </param>
    /// <param name="startMenu">
    /// <see langword="true"/> to create a Start menu shortcut.
    /// </param>
    /// <param name="desktop">
    /// <see langword="true"/> to create a desktop shortcut.
    /// </param>
    /// <returns>
    /// The full paths of the shortcuts that were created.
    /// </returns>
    public static List<string> Create(
        InstallerManifest manifest,
        string installDirectory,
        bool startMenu,
        bool desktop)
    {
        var createdShortcuts = new List<string>();

        var targetPath = Path.Combine(
            installDirectory,
            manifest.Application.Executable);

        var iconPath = Path.Combine(
            installDirectory,
            "application.ico");

        var existingIconPath = File.Exists(iconPath)
            ? iconPath
            : null;

        if (startMenu)
        {
            var startMenuPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                $"{manifest.Application.Name}.url");

            WriteInternetShortcut(
                startMenuPath,
                targetPath,
                existingIconPath);

            createdShortcuts.Add(startMenuPath);
        }

        if (desktop)
        {
            var desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{manifest.Application.Name}.url");

            WriteInternetShortcut(
                desktopPath,
                targetPath,
                existingIconPath);

            createdShortcuts.Add(desktopPath);
        }

        return createdShortcuts;
    }

    /// <summary>
    /// Writes an Internet shortcut that points to a local executable.
    /// </summary>
    /// <param name="path">Full path of the shortcut file to create.</param>
    /// <param name="target">Full path of the target executable.</param>
    /// <param name="icon">
    /// Full path of the icon to use, or <see langword="null"/> to create
    /// the shortcut without a custom icon.
    /// </param>
    private static void WriteInternetShortcut(
        string path,
        string target,
        string? icon)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var content =
            "[InternetShortcut]" + Environment.NewLine +
            $"URL=file:///{target.Replace('\\', '/')}" + Environment.NewLine;

        if (icon is not null)
        {
            content +=
                $"IconFile={icon}" + Environment.NewLine +
                "IconIndex=0" + Environment.NewLine;
        }

        File.WriteAllText(path, content);
    }
}
