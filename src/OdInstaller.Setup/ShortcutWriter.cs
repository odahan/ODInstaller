using OdInstaller.Core;
using System.IO;
namespace OdInstaller.Setup;
internal static class ShortcutWriter
{
    public static List<string> Create(InstallerManifest manifest, string installDirectory, bool startMenu, bool desktop)
    {
        var result = new List<string>(); var target = Path.Combine(installDirectory, manifest.Application.Executable);
        if (startMenu) { var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", manifest.Application.Name + ".url"); WriteInternetShortcut(path, target); result.Add(path); }
        if (desktop) { var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), manifest.Application.Name + ".url"); WriteInternetShortcut(path, target); result.Add(path); }
        return result;
    }
    // URL shortcuts avoid a broad COM dependency while remaining natively supported by Windows.
    private static void WriteInternetShortcut(string path, string target) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "[InternetShortcut]" + Environment.NewLine + "URL=file:///" + target.Replace('\\', '/') + Environment.NewLine); }
}
