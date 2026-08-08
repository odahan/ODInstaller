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
    /// Token used to cancel a running installation.
    /// </summary>
    private CancellationTokenSource? _installCts;

    /// <summary>
    /// Initializes the window and starts loading the package when the window is displayed.
    /// </summary>
    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadPackageAsync();
        Closed += (_, _) => CleanupPayloadDirectory();
    }

    /// <summary>
    /// Extracts the package embedded in the executable and initializes the user interface
    /// with its manifest. Extraction runs on a background thread so the window stays
    /// responsive while a large payload is decompressed.
    /// </summary>
    /// <remarks>
    /// The files are extracted to a unique temporary directory. The manifest, welcome image,
    /// installation directory, and shortcut options are then loaded before the first step
    /// is displayed. The manifest is revalidated against the packaged content as a
    /// defense-in-depth measure.
    /// </remarks>
    /// <exception cref="Exception">
    /// Any package reading, extraction, or validation error is logged, displayed to the user,
    /// and causes the window to close.
    /// </exception>
    private async Task LoadPackageAsync()
    {
        try
        {
            InstallerLog.Info("Extracting package.");

            // Extract the embedded package to a unique temporary directory.
            _payloadDirectory = Path.Combine(
                Path.GetTempPath(),
                $"od.installer-{Guid.NewGuid():N}");

            Directory.CreateDirectory(_payloadDirectory);

            _manifest = await Task.Run(() =>
            {
                // Read the embedded payload and extract it using safe path validation.
                using var payload = PackageFormat.OpenPayload(Environment.ProcessPath!);
                FileInventory.ExtractSafely(payload, _payloadDirectory);

                // Load the installer manifest.
                return JsonFiles.ReadManifest(
                    Path.Combine(_payloadDirectory, "installer.json"));
            });

            // Revalidate the manifest against the packaged content so a tampered
            // or malformed package is rejected before any file is written.
            var validation = ManifestValidator.ValidatePackage(
                _manifest,
                _payloadDirectory);

            if (!validation.IsValid)
            {
                InstallerLog.Error(
                    new InvalidDataException(
                        $"Invalid package manifest: {string.Join("; ", validation.Errors)}"));
                MessageBox.Show(
                    "The package is invalid:\n\n" + string.Join("\n", validation.Errors),
                    "Setup error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Close();
                return;
            }

            // Configure the welcome image when one is included in the package.
            var welcomeImage = Path.Combine(_payloadDirectory, "welcome.png");

            if (File.Exists(welcomeImage))
            {
                // Load the image fully so the temporary file is not kept locked
                // and the payload directory can be removed on close.
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.UriSource = new Uri(welcomeImage);
                image.EndInit();
                WelcomeImage.Source = image;
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
            StartMenuBox.Visibility = DesktopBox.Visibility =
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
    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_page == 1 && _manifest.License.RequireAcceptance && AcceptBox.IsChecked != true)
        {
            MessageBox.Show("You must accept the license agreement to continue.");
            return;
        }

        if (_page == 2)
        {
            _installDirectory = DirectoryBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(_installDirectory) ||
                !Path.IsPathFullyQualified(_installDirectory))
            {
                MessageBox.Show("Choose a valid absolute folder.");
                return;
            }

            if (!SafePaths.IsCompatibleInstallDirectory(_installDirectory, _manifest.Application.Id))
            {
                MessageBox.Show(
                    "The destination folder already contains files that do not belong "
                    + $"to {_manifest.Application.Name}. Choose an empty folder or the "
                    + "folder of a previous installation of this application.",
                    "Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
        }

        if (_page == 4)
        {
            if (await InstallAsync())
            {
                _page = 5;
                ShowPage();
            }

            return;
        }

        if (_page == 5)
        {
            var executablePath = Path.Combine(
                _installDirectory,
                _manifest.Application.Executable);

            if (!File.Exists(executablePath))
            {
                MessageBox.Show(
                    $"The application executable was not found:\n{executablePath}",
                    "Setup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo(executablePath)
            {
                UseShellExecute = true,
                WorkingDirectory = _installDirectory
            });

            Close();
            return;
        }

        _page++;
        ShowPage();
    }

    /// <summary>
    /// Cancels a running installation.
    /// </summary>
    /// <param name="sender">Control that raised the event.</param>
    /// <param name="e">Click event data.</param>
    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _installCts?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressText.Text = "Cancelling...";
    }

    /// <summary>
    /// Installs the application files and records its system information.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the installation completes successfully;
    /// <see langword="false"/> if the user cancels replacing an existing
    /// installation, cancels the running installation, or if an error occurs.
    /// </returns>
    /// <remarks>
    /// The method copies the application files, icon, and uninstaller, creates the requested
    /// shortcuts, registers the application in the Windows Registry, and writes the installation
    /// manifest. Copying runs on a background thread with progress reporting and cancellation.
    /// On failure or cancellation, every file, shortcut, and directory created is removed.
    /// </remarks>
    private async Task<bool> InstallAsync()
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

        if (!SafePaths.IsCompatibleInstallDirectory(_installDirectory, _manifest.Application.Id))
        {
            MessageBox.Show(
                "The destination folder already contains files that do not belong "
                + $"to {_manifest.Application.Name}. Choose an empty folder or the "
                + "folder of a previous installation of this application.",
                "Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        var createdShortcuts = new List<string>();

        // Snapshot the shortcuts that already exist so rollback only removes
        // the ones that were actually created by this installation.
        var plannedShortcuts = ShortcutWriter.GetShortcutPaths(
            _manifest,
            StartMenuBox.IsChecked == true,
            DesktopBox.IsChecked == true);

        var preExistingShortcuts = plannedShortcuts
            .Where(File.Exists)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var installDirectoryExisted = Directory.Exists(_installDirectory);

        _installCts = new CancellationTokenSource();

        try
        {
            ShowInstalling(true);

            InstallerLog.Info($"Installing to {_installDirectory}.");
            Directory.CreateDirectory(_installDirectory);

            var appSource = Path.Combine(_payloadDirectory, "app");
            var total = FileInventory.Enumerate(appSource).Count;

            var progress = new Progress<FileCopyProgress>(update =>
            {
                InstallProgress.Value = total == 0
                    ? 100
                    : (double)update.Current / total * 100;
                ProgressText.Text =
                    $"Copying files ({update.Current} of {total}): {update.RelativePath}";
            });

            var copy = await Task.Run(
                () => FileInventory.CopySafely(
                    appSource,
                    _installDirectory,
                    progress,
                    _installCts.Token),
                _installCts.Token);

            createdFiles.AddRange(copy.Files);
            createdDirectories.AddRange(copy.CreatedDirectories);

            var packagedIcon = Path.Combine(_payloadDirectory, "application.ico");
            if (File.Exists(packagedIcon))
            {
                File.Copy(
                    packagedIcon,
                    Path.Combine(_installDirectory, "application.ico"),
                    true);

                createdFiles.Add("application.ico");
            }

            var uninstaller = Path.Combine(
                _installDirectory,
                "OD.Installer.Uninstaller.exe");

            File.Copy(
                Path.Combine(_payloadDirectory, "uninstaller", "OD.Installer.Uninstaller.exe"),
                uninstaller,
                true);

            createdFiles.Add("OD.Installer.Uninstaller.exe");

            foreach (var shortcut in ShortcutWriter.Create(
                _manifest,
                _installDirectory,
                StartMenuBox.IsChecked == true,
                DesktopBox.IsChecked == true))
            {
                if (!preExistingShortcuts.Contains(shortcut))
                {
                    createdShortcuts.Add(shortcut);
                }
            }

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
                    Files = createdFiles,
                    Directories = Directory
                        .GetDirectories(_installDirectory, "*", SearchOption.AllDirectories)
                        .Select(x => Path.GetRelativePath(_installDirectory, x))
                        .ToList(),
                    Shortcuts = plannedShortcuts,
                    RegistryKeys = [uninstallKey],
                    UninstallerPath = uninstaller
                });

            InstallerLog.Info("Installation completed.");
            InstallProgress.Value = 100;
            return true;
        }
        catch (OperationCanceledException)
        {
            InstallerLog.Info("Installation cancelled.");
            Rollback(
                _installDirectory,
                installDirectoryExisted,
                createdFiles,
                createdDirectories,
                createdShortcuts,
                uninstallKey);
            MessageBox.Show(
                "The installation was cancelled.",
                "Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }
        catch (Exception ex)
        {
            InstallerLog.Error(ex);
            Rollback(
                _installDirectory,
                installDirectoryExisted,
                createdFiles,
                createdDirectories,
                createdShortcuts,
                uninstallKey);
            MessageBox.Show(
                $"Installation failed:\n\n{ex.Message}",
                "Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _installCts.Dispose();
            _installCts = null;
            ShowInstalling(false);
        }
    }

    /// <summary>
    /// Shows or hides the installation progress area and disables navigation
    /// while an installation is running.
    /// </summary>
    /// <param name="installing"><see langword="true"/> while files are copied.</param>
    private void ShowInstalling(bool installing)
    {
        BackButton.IsEnabled = !installing;
        NextButton.IsEnabled = !installing;
        CancelButton.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = installing ? Visibility.Visible : Visibility.Collapsed;

        if (installing)
        {
            InstallProgress.Value = 0;
            ProgressText.Text = "";
            CancelButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Removes everything created by a failed or cancelled installation:
    /// copied files, new shortcuts, newly created directories, and the
    /// uninstall registry key.
    /// </summary>
    /// <param name="installDirectory">Target installation directory.</param>
    /// <param name="installDirectoryExisted">
    /// <see langword="true"/> when the directory existed before the installation.
    /// </param>
    /// <param name="createdFiles">Relative paths of the copied files.</param>
    /// <param name="createdDirectories">Full paths of the created directories.</param>
    /// <param name="createdShortcuts">Full paths of the created shortcuts.</param>
    /// <param name="uninstallKey">Uninstall registry key to remove.</param>
    private static void Rollback(
        string installDirectory,
        bool installDirectoryExisted,
        IEnumerable<string> createdFiles,
        IEnumerable<string> createdDirectories,
        IEnumerable<string> createdShortcuts,
        string uninstallKey)
    {
        foreach (var relative in createdFiles)
        {
            var path = Path.Combine(installDirectory, relative);
            TryDeleteFile(path);
        }

        foreach (var shortcut in createdShortcuts)
        {
            TryDeleteFile(shortcut);
        }

        // Remove the created directories, deepest first, only when empty.
        foreach (var directory in createdDirectories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(directory => directory.Length))
        {
            TryDeleteDirectoryIfEmpty(directory);
        }

        // Remove the root when it was created by this installation.
        if (!installDirectoryExisted)
        {
            TryDeleteDirectoryIfEmpty(installDirectory);
        }

        Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, false);
    }

    /// <summary>
    /// Deletes a file, ignoring locking or permission failures so a rollback
    /// can continue.
    /// </summary>
    private static void TryDeleteFile(string path)
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
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes an empty directory, ignoring failures so a rollback can continue.
    /// </summary>
    private static void TryDeleteDirectoryIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory)
                && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Removes the temporary payload directory extracted at startup. Called
    /// when the window closes, whether the installation succeeded or failed.
    /// </summary>
    private void CleanupPayloadDirectory()
    {
        try
        {
            if (!string.IsNullOrEmpty(_payloadDirectory) &&
                Directory.Exists(_payloadDirectory))
            {
                Directory.Delete(_payloadDirectory, true);
            }
        }
        catch (IOException)
        {
            // A file is still locked; the temporary directory is left behind.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup.
        }
    }
}
