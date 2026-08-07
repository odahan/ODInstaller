using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using OD.Installer.Core;

namespace OD.Installer.Setup;

/// <summary>
/// Creates Windows shortcuts for the installed application.
/// </summary>
/// <remarks>
/// Shortcuts are real <c>.lnk</c> files created through the shell's
/// <c>IShellLink</c> COM interface, which is part of Windows and requires
/// no external dependency. Setting the working directory is important for
/// applications that depend on their own folder at launch.
/// </remarks>
internal static class ShortcutWriter
{
    /// <summary>
    /// CLSID of the ShellLink COM class.
    /// </summary>
    private static readonly Guid ClsidShellLink =
        new("00021401-0000-0000-C000-000000000046");

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
                $"{manifest.Application.Name}.lnk");

            WriteShortcut(
                startMenuPath,
                manifest.Application.Name,
                targetPath,
                installDirectory,
                existingIconPath);

            createdShortcuts.Add(startMenuPath);
        }

        if (desktop)
        {
            var desktopPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                $"{manifest.Application.Name}.lnk");

            WriteShortcut(
                desktopPath,
                manifest.Application.Name,
                targetPath,
                installDirectory,
                existingIconPath);

            createdShortcuts.Add(desktopPath);
        }

        return createdShortcuts;
    }

    /// <summary>
    /// Writes a <c>.lnk</c> shortcut pointing to the target executable.
    /// </summary>
    /// <param name="path">Full path of the shortcut file to create.</param>
    /// <param name="description">Description shown for the shortcut.</param>
    /// <param name="target">Full path of the target executable.</param>
    /// <param name="workingDirectory">Working directory set on the shortcut.</param>
    /// <param name="icon">
    /// Full path of the icon to use, or <see langword="null"/> to let the
    /// shortcut use the executable's own icon.
    /// </param>
    private static void WriteShortcut(
        string path,
        string description,
        string target,
        string workingDirectory,
        string? icon)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var shellLink = (IShellLinkW)Activator.CreateInstance(
            Type.GetTypeFromCLSID(ClsidShellLink)!)!;

        try
        {
            shellLink.SetPath(target);
            shellLink.SetWorkingDirectory(workingDirectory);
            shellLink.SetDescription(description);
            shellLink.SetShowCmd(1);

            if (icon is not null)
            {
                shellLink.SetIconLocation(icon, 0);
            }

            ((IPersistFile)shellLink).Save(path, false);
        }
        finally
        {
            Marshal.FinalReleaseComObject(shellLink);
        }
    }
}

/// <summary>
/// Shell's <c>IShellLinkW</c> interface used to create real shortcuts.
/// </summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath(
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
        int cch,
        IntPtr pfd,
        int fFlags);

    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);

    void GetDescription(
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
        int cch);

    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

    void GetWorkingDirectory(
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
        int cch);

    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

    void GetArguments(
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
        int cch);

    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);

    void GetIconLocation(
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
        int cch,
        out int piIcon);

    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);

    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);

    void Resolve(IntPtr hwnd, int fFlags);

    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}
