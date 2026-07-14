using Microsoft.Win32;
using OdInstaller.Core;
using System.Diagnostics;
using System.IO.Compression;
using System.Windows;
using System.IO;
using System.Linq;

namespace OdInstaller.Setup;
public partial class MainWindow : Window
{
    private InstallerManifest _manifest = null!; private int _page; private string _payloadDirectory = ""; private string _installDirectory = "";
    public MainWindow() { InitializeComponent(); Loaded += (_, _) => LoadPackage(); }
    private void LoadPackage()
    {
        try { _payloadDirectory = Path.Combine(Path.GetTempPath(), "odinstaller-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(_payloadDirectory); using var payload = PackageFormat.OpenPayload(Environment.ProcessPath!); FileInventory.ExtractSafely(payload, _payloadDirectory); _manifest = JsonFiles.ReadManifest(Path.Combine(_payloadDirectory, "installer.json")); _installDirectory = SafePaths.ResolveInstallDirectory(_manifest.Installation.Directory, _manifest.Application.Name); StartMenuBox.IsChecked = _manifest.Shortcuts.StartMenu; DesktopBox.IsChecked = _manifest.Shortcuts.Desktop; ShowPage(); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Setup error", MessageBoxButton.OK, MessageBoxImage.Error); Close(); }
    }
    private void ShowPage()
    {
        LicensePanel.Visibility = DirectoryBox.Visibility = AcceptBox.Visibility = StartMenuBox.Visibility = DesktopBox.Visibility = Progress.Visibility = Visibility.Collapsed; BackButton.IsEnabled = _page > 0; NextButton.Content = "Next";
        if (_page == 0) { TitleText.Text = $"Welcome to { _manifest.Application.Name }"; BodyText.Text = "This wizard will install the application for the current user."; }
        else if (_page == 1) { TitleText.Text = "License agreement"; LicenseText.Text = File.ReadAllText(Path.Combine(_payloadDirectory, "LICENSE.txt")); LicensePanel.Visibility = Visibility.Visible; AcceptBox.Visibility = _manifest.License.RequireAcceptance ? Visibility.Visible : Visibility.Collapsed; BodyText.Text = ""; }
        else if (_page == 2) { TitleText.Text = "Installation folder"; BodyText.Text = "Choose where the application will be installed."; DirectoryBox.Text = _installDirectory; DirectoryBox.Visibility = _manifest.Installation.AllowDirectorySelection ? Visibility.Visible : Visibility.Collapsed; }
        else if (_page == 3) { TitleText.Text = "Shortcuts"; BodyText.Text = "Choose the shortcuts to create."; StartMenuBox.Visibility = DesktopBox.Visibility = Visibility.Visible; }
        else if (_page == 4) { TitleText.Text = "Ready to install"; BodyText.Text = "Click Install to copy files and register the application."; NextButton.Content = "Install"; }
        else { TitleText.Text = "Installation complete"; BodyText.Text = $"{_manifest.Application.Name} has been installed."; NextButton.Content = "Launch"; BackButton.Visibility = Visibility.Collapsed; }
    }
    private void Back_Click(object sender, RoutedEventArgs e) { _page--; ShowPage(); }
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 1 && _manifest.License.RequireAcceptance && AcceptBox.IsChecked != true) { MessageBox.Show("You must accept the license agreement to continue."); return; }
        if (_page == 2) { _installDirectory = DirectoryBox.Text; if (string.IsNullOrWhiteSpace(_installDirectory) || !Path.IsPathFullyQualified(_installDirectory)) { MessageBox.Show("Choose a valid absolute folder."); return; } }
        if (_page == 4) { if (Install()) { _page = 5; ShowPage(); } return; }
        if (_page == 5) { Process.Start(new ProcessStartInfo(Path.Combine(_installDirectory, _manifest.Application.Executable)) { UseShellExecute = true }); Close(); return; }
        _page++; ShowPage();
    }
    private bool Install()
    {
        var uninstallKey = $"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{_manifest.Application.Id}";
        if (Registry.CurrentUser.OpenSubKey(uninstallKey) is not null && MessageBox.Show("An installation with this id already exists. Replace it?", "Existing installation", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return false;
        var created = new List<string>();
        try { Directory.CreateDirectory(_installDirectory); CopyDirectory(Path.Combine(_payloadDirectory, "app"), _installDirectory, created); var uninstaller = Path.Combine(_installDirectory, "OdInstaller.Uninstaller.exe"); File.Copy(Path.Combine(_payloadDirectory, "uninstaller", "OdInstaller.Uninstaller.exe"), uninstaller, true); created.Add("OdInstaller.Uninstaller.exe"); var shortcuts = ShortcutWriter.Create(_manifest, _installDirectory, StartMenuBox.IsChecked == true, DesktopBox.IsChecked == true); using var key = Registry.CurrentUser.CreateSubKey(uninstallKey); key.SetValue("DisplayName", _manifest.Application.Name); key.SetValue("DisplayVersion", _manifest.Application.Version); key.SetValue("Publisher", _manifest.Application.Publisher); key.SetValue("InstallLocation", _installDirectory); key.SetValue("DisplayIcon", Path.Combine(_installDirectory, _manifest.Application.Executable)); key.SetValue("UninstallString", $"\"{uninstaller}\""); key.SetValue("NoModify", 1, RegistryValueKind.DWord); key.SetValue("NoRepair", 1, RegistryValueKind.DWord); JsonFiles.WriteInstalledManifest(Path.Combine(_installDirectory, ".odinstaller-installed.json"), new InstalledManifest { ApplicationId = _manifest.Application.Id, Version = _manifest.Application.Version, InstallDirectory = _installDirectory, Files = created, Directories = Directory.GetDirectories(_installDirectory, "*", SearchOption.AllDirectories).Select(x => Path.GetRelativePath(_installDirectory, x)).ToList(), Shortcuts = shortcuts, RegistryKeys = [uninstallKey], UninstallerPath = uninstaller }); return true; }
        catch { foreach (var relative in created) { var path = Path.Combine(_installDirectory, relative); if (File.Exists(path)) File.Delete(path); } Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, false); throw; }
    }
    private static void CopyDirectory(string source, string target, List<string> created) { foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var relative = Path.GetRelativePath(source, file); if (!SafePaths.TryResolveUnderRoot(target, relative, out var destination)) throw new InvalidDataException("Unsafe file path."); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination, true); created.Add(relative); } }
}
