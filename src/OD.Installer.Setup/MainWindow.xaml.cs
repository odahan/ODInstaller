using Microsoft.Win32;
using OD.Installer.Core;
using System.Diagnostics;
using System.Windows;
using System.IO;
using System.Windows.Media.Imaging;

namespace OD.Installer.Setup;

/// <summary>
/// Main window of the installation wizard.
/// </summary>
/// <remarks>
/// Loads the package embedded in the executable, displays the configuration steps,
/// installs the application, and records the information required for uninstallation.
/// </remarks>
public partial class MainWindow : Window
{
    /// <summary>
    /// Application manifest extracted from the installation package.
    /// </summary>
    private InstallerManifest _manifest = null!;

    /// <summary>
    /// Index of the step currently displayed in the wizard.
    /// </summary>
    private int _page;

    /// <summary>
    /// Temporary directory containing the files extracted from the package.
    /// </summary>
    private string _payloadDirectory = "";

    /// <summary>
    /// Target installation directory.
    /// </summary>
    private string _installDirectory = "";

    /// <summary>
    /// Initializes the window and starts loading the package when the window is displayed.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadPackage();
    }

    /// <summary>
    /// Extracts the package embedded in the executable and initializes the user interface
    /// with its manifest.
    /// </summary>
    /// <remarks>
    /// The files are extracted to a unique temporary directory. The manifest, welcome image,
    /// installation directory, and shortcut options are then loaded before the first step
    /// is displayed.
    /// </remarks>
    /// <exception cref="Exception">
    /// Any package reading, extraction, or validation error is logged, displayed to the user,
    /// and causes the window to close.
    /// </exception>
    private void LoadPackage()
    {
        try
        {
            InstallerLog.Info("Extracting package.");
            // Extract the embedded package to a unique temporary directory.
            _payloadDirectory = Path.Combine(
                Path.GetTempPath(),
                $"od.installer-{Guid.NewGuid():N}");

            Directory.CreateDirectory(_payloadDirectory);

            // Read the embedded payload and extract it using safe path validation.
            using var payload = PackageFormat.OpenPayload(Environment.ProcessPath!);
            FileInventory.ExtractSafely(payload, _payloadDirectory);

            // Load the installer manifest and initialize the wizard options.
            _manifest = JsonFiles.ReadManifest(
                Path.Combine(_payloadDirectory, "installer.json"));

            // Configure the welcome image when one is included in the package.
            var welcomeImage = Path.Combine(_payloadDirectory, "welcome.png");

            if (File.Exists(welcomeImage))
            {
                WelcomeImage.Source = new BitmapImage(new Uri(welcomeImage));
            }

            // Resolve the default installation directory and shortcut selections.
            _installDirectory = SafePaths.ResolveInstallDirectory(
                _manifest.Installation.Directory,
                _manifest.Application.Name);

            StartMenuBox.IsChecked = _manifest.Shortcuts.StartMenu;
            DesktopBox.IsChecked = _manifest.Shortcuts.Desktop;

            InstallerLog.Info(
                $"Package loaded for {_manifest.Application.Name} {_manifest.Application.Version}.");

            ShowPage();
        }
        catch (Exception ex)
        {
            InstallerLog.Error(ex);
            MessageBox.Show(ex.Message, "Setup error", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    /// <summary>
    /// Configures the controls displayed for the wizard's active step.
    /// </summary>
    private void ShowPage()
    {
        LicensePanel.Visibility = DirectoryBox.Visibility = AcceptBox.Visibility =
            StartMenuBox.Visibility = DesktopBox.Visibility = Progress.Visibility =
            WelcomeImage.Visibility = Visibility.Collapsed;

        WelcomePanel.Visibility = Visibility.Collapsed;
        StandardPanel.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Visible;
        BackButton.IsEnabled = _page > 0;
        NextButton.Content = "Next";

        if (_page == 0)
        {
            StandardPanel.Visibility = Visibility.Collapsed;
            WelcomePanel.Visibility = Visibility.Visible;
            TitleText.Text = $"Welcome to {_manifest.Application.Name}";
            BodyText.Text = "This wizard will install the application for the current user.";

            var hasWelcomeImage = WelcomeImage.Source is not null;
            WelcomeImageRow.Height = hasWelcomeImage
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(0);

            WelcomePanel.VerticalAlignment = hasWelcomeImage
                ? VerticalAlignment.Stretch
                : VerticalAlignment.Center;

            if (hasWelcomeImage)
            {
                WelcomeImage.Visibility = Visibility.Visible;
            }
        }
        else if (_page == 1)
        {
            StandardTitleText.Text = "License agreement";
            LicenseText.Text = File.ReadAllText(Path.Combine(_payloadDirectory, "LICENSE.txt"));
            LicensePanel.Visibility = Visibility.Visible;
            AcceptBox.Visibility = _manifest.License.RequireAcceptance
                ? Visibility.Visible
                : Visibility.Collapsed;
            StandardBodyText.Text = "";
        }
        else if (_page == 2)
        {
            StandardTitleText.Text = "Installation folder";
            StandardBodyText.Text = "Choose where the application will be installed.";
            DirectoryBox.Text = _installDirectory;
            DirectoryBox.Visibility = _manifest.Installation.AllowDirectorySelection
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        else if (_page == 3)
        {
            StandardTitleText.Text = "Shortcuts";
            StandardBodyText.Text = "Choose the shortcuts to create.";
            StartMenuBox.Visibility = DesktopBox.Visibility = Visibility.Visible;
        }
        else if (_page == 4)
        {
            StandardTitleText.Text = "Ready to install";
            StandardBodyText.Text = "Click Install to copy files and register the application.";
            NextButton.Content = "Install";
        }
        else if (_page == 5)
        {
            StandardTitleText.Text = "Installation complete";
            StandardBodyText.Text = $"{_manifest.Application.Name} has been installed.";
            NextButton.Content = "Launch";
            BackButton.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Returns to the previous step in the wizard.
    /// </summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Click event data.</param>
    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _page--;
        ShowPage();
    }

    /// <summary>
    /// Validates the current step and moves to the next step, starts the installation,
    /// or launches the installed application according to the wizard state.
    /// </summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Click event data.</param>
    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 1 && _manifest.License.RequireAcceptance && AcceptBox.IsChecked != true)
        {
            MessageBox.Show("You must accept the license agreement to continue.");
            return;
        }

        if (_page == 2)
        {
            _installDirectory = DirectoryBox.Text;

            if (string.IsNullOrWhiteSpace(_installDirectory) ||
                !Path.IsPathFullyQualified(_installDirectory))
            {
                MessageBox.Show("Choose a valid absolute folder.");
                return;
            }
        }

        if (_page == 4)
        {
            if (Install())
            {
                _page = 5;
                ShowPage();
            }

            return;
        }

        if (_page == 5)
        {
            Process.Start(new ProcessStartInfo(
                Path.Combine(_installDirectory, _manifest.Application.Executable))
            {
                UseShellExecute = true
            });

            Close();
            return;
        }

        _page++;
        ShowPage();
    }

    /// <summary>
    /// Installs the application files and records its system information.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the installation completes successfully;
    /// <see langword="false"/> if the user cancels replacing an existing installation.
    /// </returns>
    /// <remarks>
    /// The method copies the application files, icon, and uninstaller, creates the requested
    /// shortcuts, registers the application in the Windows Registry, and writes the installation
    /// manifest.
    /// </remarks>
    /// <exception cref="Exception">
    /// An installation error is logged, the created files are removed, and the exception
    /// is propagated to the caller.
    /// </exception>
    private bool Install()
    {
        var uninstallKey =
            $"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{_manifest.Application.Id}";

        if (Registry.CurrentUser.OpenSubKey(uninstallKey) is not null &&
            MessageBox.Show(
                "An installation with this id already exists. Replace it?",
                "Existing installation",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes)
        {
            return false;
        }

        var created = new List<string>();

        try
        {
            InstallerLog.Info($"Installing to {_installDirectory}.");
            Directory.CreateDirectory(_installDirectory);

            CopyDirectory(
                Path.Combine(_payloadDirectory, "app"),
                _installDirectory,
                created);

            var packagedIcon = Path.Combine(_payloadDirectory, "application.ico");
            if (File.Exists(packagedIcon))
            {
                File.Copy(
                    packagedIcon,
                    Path.Combine(_installDirectory, "application.ico"),
                    true);

                created.Add("application.ico");
            }

            var uninstaller = Path.Combine(
                _installDirectory,
                "OD.Installer.Uninstaller.exe");

            File.Copy(
                Path.Combine(_payloadDirectory, "uninstaller", "OD.Installer.Uninstaller.exe"),
                uninstaller,
                true);

            created.Add("OD.Installer.Uninstaller.exe");

            var shortcuts = ShortcutWriter.Create(
                _manifest,
                _installDirectory,
                StartMenuBox.IsChecked == true,
                DesktopBox.IsChecked == true);

            using var key = Registry.CurrentUser.CreateSubKey(uninstallKey);
            key.SetValue("DisplayName", _manifest.Application.Name);
            key.SetValue("DisplayVersion", _manifest.Application.Version);
            key.SetValue("Publisher", _manifest.Application.Publisher);
            key.SetValue(
                "InstallDate",
                DateTime.Today.ToString(
                    "yyyyMMdd",
                    System.Globalization.CultureInfo.InvariantCulture));
            key.SetValue("InstallLocation", _installDirectory);
            key.SetValue(
                "DisplayIcon",
                File.Exists(Path.Combine(_installDirectory, "application.ico"))
                    ? Path.Combine(_installDirectory, "application.ico")
                    : Path.Combine(_installDirectory, _manifest.Application.Executable));
            key.SetValue("UninstallString", $"\"{uninstaller}\"");
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            JsonFiles.WriteInstalledManifest(
                Path.Combine(_installDirectory, ".od.installer-installed.json"),
                new InstalledManifest
                {
                    ApplicationId = _manifest.Application.Id,
                    Version = _manifest.Application.Version,
                    InstallDirectory = _installDirectory,
                    Files = created,
                    Directories = Directory
                        .GetDirectories(_installDirectory, "*", SearchOption.AllDirectories)
                        .Select(x => Path.GetRelativePath(_installDirectory, x))
                        .ToList(),
                    Shortcuts = shortcuts,
                    RegistryKeys = [uninstallKey],
                    UninstallerPath = uninstaller
                });

            InstallerLog.Info("Installation completed.");
            return true;
        }
        catch (Exception ex)
        {
            InstallerLog.Error(ex);

            foreach (var relative in created)
            {
                var path = Path.Combine(_installDirectory, relative);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, false);
            throw;
        }
    }

    /// <summary>
    /// Recursively copies files from a directory to a target directory.
    /// </summary>
    /// <param name="source">Source directory containing the files to copy.</param>
    /// <param name="target">Destination root directory.</param>
    /// <param name="created">
    /// Collection to populate with the relative paths of copied files,
    /// allowing cleanup if the operation fails.
    /// </param>
    /// <exception cref="InvalidDataException">
    /// Thrown when a relative path attempts to escape the target directory.
    /// </exception>
    private static void CopyDirectory(
        string source,
        string target,
        List<string> created)
    {
        foreach (var file in Directory.EnumerateFiles(
            source,
            "*",
            SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);

            if (!SafePaths.TryResolveUnderRoot(
                target,
                relative,
                out var destination))
            {
                throw new InvalidDataException("Unsafe file path.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
            created.Add(relative);
        }
    }
}
