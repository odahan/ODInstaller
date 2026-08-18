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
    /// Installs or updates the application and records its system information.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if the installation completes successfully;
    /// <see langword="false"/> if the user cancels updating an existing
    /// installation, cancels the running installation, or if an error occurs.
    /// </returns>
    /// <remarks>
    /// The new application directory is prepared beside the active version and
    /// swapped only after every file is ready. The previous directory and every
    /// overwritten external file remain available until shortcuts and registry
    /// registration succeed, allowing the complete installation to be restored.
    /// </remarks>
    private async Task<bool> InstallAsync()
    {
        var uninstallKey =
            $"Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\{_manifest.Application.Id}";

        if (!SafePaths.IsCompatibleInstallDirectory(
                _installDirectory,
                _manifest.Application.Id))
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

        var previousInstallation = ReadPreviousInstallation(_installDirectory);
        bool registeredInstallationExists;
        string? registeredLocation;

        using (var existingRegistryKey = Registry.CurrentUser.OpenSubKey(uninstallKey))
        {
            registeredInstallationExists = existingRegistryKey is not null;
            registeredLocation = existingRegistryKey?.GetValue("InstallLocation") as string;
        }

        if (!string.IsNullOrWhiteSpace(registeredLocation)
            && !PathsEqual(registeredLocation, _installDirectory))
        {
            MessageBox.Show(
                $"{_manifest.Application.Name} is already registered in another folder:"
                + $"\n\n{registeredLocation}\n\nUse that folder for the update or "
                + "uninstall the existing version first.",
                "Existing installation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (previousInstallation is not null
            && !string.IsNullOrWhiteSpace(previousInstallation.InstallDirectory)
            && !PathsEqual(previousInstallation.InstallDirectory, _installDirectory))
        {
            MessageBox.Show(
                "The installed manifest refers to another installation folder. "
                + "Uninstall the existing version before choosing a new folder.",
                "Existing installation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (!ConfirmInstallOrUpdate(
                previousInstallation,
                registeredInstallationExists))
        {
            return false;
        }

        if (previousInstallation is not null
            && !CanAcquireExclusiveAccess(
                _installDirectory,
                out var inaccessibleFile))
        {
            MessageBox.Show(
                $"The current installation is still in use or cannot be modified:"
                + $"\n\n{inaccessibleFile}\n\nClose {_manifest.Application.Name} "
                + "and try again.",
                "Application in use",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var registrySnapshot = CaptureRegistry(uninstallKey);
        var plannedShortcuts = ShortcutWriter.GetShortcutPaths(
            _manifest,
            StartMenuBox.IsChecked == true,
            DesktopBox.IsChecked == true);
        var rootFiles = new List<string>();
        var externalFiles = new List<string>();
        var externalDirectories = new List<string>();
        var fileBackups = new FileBackupTransaction();
        InstallationDirectoryTransaction? directoryTransaction = null;
        var registryChanged = false;
        _installCts = new CancellationTokenSource();

        try
        {
            ShowInstalling(true);
            directoryTransaction = new InstallationDirectoryTransaction(
                _installDirectory);

            InstallerLog.Info(
                previousInstallation is null
                    ? $"Installing to {_installDirectory}."
                    : $"Updating {_manifest.Application.Name} from "
                      + $"{previousInstallation.Version} to {_manifest.Application.Version}.");

            var folders = _manifest.Source.EffectiveFolders();
            var appRoot = Path.Combine(_payloadDirectory, "app");
            var copies = new List<(string Source, string Target, string Prefix, bool External)>();
            var total = 0;

            for (var index = 0; index < folders.Count; index++)
            {
                var folder = folders[index];

                if (SafePaths.IsExternalDestination(folder.Destination))
                {
                    var destination = SafePaths.ResolveInstallDirectory(
                        folder.Destination,
                        _manifest.Application.Name);

                    if (!IsCompatibleExternalDirectory(
                            destination,
                            previousInstallation))
                    {
                        MessageBox.Show(
                            "The external destination already contains files that "
                            + $"do not belong to {_manifest.Application.Name}:"
                            + $"\n\n{destination}",
                            "Setup",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        directoryTransaction.Rollback();
                        return false;
                    }

                    var source = Path.Combine(
                        _payloadDirectory,
                        "external",
                        index.ToString(
                            System.Globalization.CultureInfo.InvariantCulture));

                    if (Directory.Exists(source))
                    {
                        total += FileInventory.Enumerate(source).Count;
                        copies.Add((source, destination, "", true));
                    }

                    continue;
                }

                var relative = SafePaths.IsRootDestination(folder.Destination)
                    ? ""
                    : folder.Destination.Trim('/', '\\');
                var sourceDirectory = relative.Length == 0
                    ? appRoot
                    : Path.Combine(appRoot, relative);
                var targetDirectory = relative.Length == 0
                    ? directoryTransaction.StagingDirectory
                    : Path.Combine(directoryTransaction.StagingDirectory, relative);

                if (Directory.Exists(sourceDirectory))
                {
                    total += FileInventory.Enumerate(sourceDirectory).Count;
                    copies.Add((sourceDirectory, targetDirectory, relative, false));
                }
            }

            IProgress<FileCopyProgress> progress = new Progress<FileCopyProgress>(update =>
            {
                InstallProgress.Value = total == 0
                    ? 100
                    : (double)update.Current / total * 100;
                ProgressText.Text =
                    $"Copying files ({update.Current} of {total}): {update.RelativePath}";
            });

            await Task.Run(
                () =>
                {
                    var offset = 0;

                    foreach (var (source, target, prefix, external) in copies)
                    {
                        IProgress<FileCopyProgress> folderProgress =
                            new Progress<FileCopyProgress>(
                                update => progress.Report(new FileCopyProgress(
                                    offset + update.Current,
                                    total,
                                    update.RelativePath)));

                        var result = FileInventory.CopySafely(
                            source,
                            target,
                            folderProgress,
                            _installCts.Token,
                            external ? fileBackups.Capture : null);

                        if (external)
                        {
                            externalFiles.AddRange(
                                result.Files.Select(file => Path.Combine(target, file)));
                            externalDirectories.AddRange(result.CreatedDirectories);
                        }
                        else
                        {
                            rootFiles.AddRange(
                                prefix.Length == 0
                                    ? result.Files
                                    : result.Files.Select(file => Path.Combine(prefix, file)));
                        }

                        offset += result.Files.Count;
                    }
                },
                _installCts.Token);

            var packagedIcon = Path.Combine(_payloadDirectory, "application.ico");

            if (File.Exists(packagedIcon))
            {
                File.Copy(
                    packagedIcon,
                    Path.Combine(
                        directoryTransaction.StagingDirectory,
                        "application.ico"),
                    overwrite: true);
                rootFiles.Add("application.ico");
            }

            var uninstaller = Path.Combine(
                _installDirectory,
                "OD.Installer.Uninstaller.exe");

            File.Copy(
                Path.Combine(
                    _payloadDirectory,
                    "uninstaller",
                    "OD.Installer.Uninstaller.exe"),
                Path.Combine(
                    directoryTransaction.StagingDirectory,
                    "OD.Installer.Uninstaller.exe"),
                overwrite: true);
            rootFiles.Add("OD.Installer.Uninstaller.exe");

            var allExternalFiles = (previousInstallation?.ExternalFiles ?? [])
                .Concat(externalFiles)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var allExternalDirectories =
                (previousInstallation?.ExternalDirectories ?? [])
                .Concat(externalDirectories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            JsonFiles.WriteInstalledManifest(
                Path.Combine(
                    directoryTransaction.StagingDirectory,
                    ".od.installer-installed.json"),
                new InstalledManifest
                {
                    ApplicationId = _manifest.Application.Id,
                    Version = _manifest.Application.Version,
                    InstallDirectory = _installDirectory,
                    Files = rootFiles,
                    Directories = Directory
                        .GetDirectories(
                            directoryTransaction.StagingDirectory,
                            "*",
                            SearchOption.AllDirectories)
                        .Select(directory => Path.GetRelativePath(
                            directoryTransaction.StagingDirectory,
                            directory))
                        .ToList(),
                    ExternalFiles = allExternalFiles,
                    ExternalDirectories = allExternalDirectories,
                    Shortcuts = plannedShortcuts,
                    RegistryKeys = [uninstallKey],
                    UninstallerPath = uninstaller
                });

            ProgressText.Text = "Activating the new version...";
            directoryTransaction.Swap();

            var previousShortcuts = previousInstallation?.Shortcuts ?? [];

            foreach (var shortcut in previousShortcuts
                         .Concat(plannedShortcuts)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                fileBackups.Capture(shortcut);
            }

            foreach (var obsoleteShortcut in previousShortcuts.Except(
                         plannedShortcuts,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(obsoleteShortcut))
                {
                    File.Delete(obsoleteShortcut);
                }
            }

            ShortcutWriter.Create(
                _manifest,
                _installDirectory,
                StartMenuBox.IsChecked == true,
                DesktopBox.IsChecked == true);

            registryChanged = true;
            WriteRegistry(uninstallKey, uninstaller);

            if (!directoryTransaction.Complete())
            {
                InstallerLog.Info(
                    $"The previous installation backup could not be removed: "
                    + directoryTransaction.BackupDirectory);
            }

            if (!fileBackups.Complete())
            {
                InstallerLog.Info(
                    "Temporary rollback files could not be removed.");
            }

            InstallerLog.Info("Installation completed.");
            InstallProgress.Value = 100;
            return true;
        }
        catch (OperationCanceledException)
        {
            InstallerLog.Info("Installation cancelled.");
            var rollbackSucceeded = RollbackInstallation(
                directoryTransaction,
                fileBackups,
                externalDirectories,
                uninstallKey,
                registrySnapshot,
                registryChanged);
            MessageBox.Show(
                rollbackSucceeded
                    ? "The installation was cancelled. The previous version has been restored."
                    : "The installation was cancelled, but automatic restoration was incomplete. "
                      + "See the installation log for details.",
                "Setup",
                MessageBoxButton.OK,
                rollbackSucceeded
                    ? MessageBoxImage.Information
                    : MessageBoxImage.Error);
            return false;
        }
        catch (Exception ex)
        {
            InstallerLog.Error(ex);
            var rollbackSucceeded = RollbackInstallation(
                directoryTransaction,
                fileBackups,
                externalDirectories,
                uninstallKey,
                registrySnapshot,
                registryChanged);
            var closeApplicationHint = ex is IOException or UnauthorizedAccessException
                ? "\n\nClose the application and try again."
                : "";
            var rollbackMessage = rollbackSucceeded
                ? "\n\nThe previous version has been restored."
                : "\n\nAutomatic restoration was incomplete. See the installation log.";

            MessageBox.Show(
                $"Installation failed:\n\n{ex.Message}{closeApplicationHint}{rollbackMessage}",
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
    /// Reads the installed manifest from an existing target directory.
    /// </summary>
    private static InstalledManifest? ReadPreviousInstallation(string directory)
    {
        var path = Path.Combine(directory, ".od.installer-installed.json");
        return File.Exists(path)
            ? JsonFiles.ReadInstalledManifest(path)
            : null;
    }

    /// <summary>
    /// Asks the user to confirm an update, reinstall, downgrade, or repair.
    /// </summary>
    private bool ConfirmInstallOrUpdate(
        InstalledManifest? previousInstallation,
        bool registeredInstallationExists)
    {
        if (previousInstallation is null)
        {
            return !registeredInstallationExists
                || MessageBox.Show(
                    $"A registered installation of {_manifest.Application.Name} was found, "
                    + "but its installed files are missing. Reinstall it?",
                    "Repair installation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        var packageVersion = Version.Parse(_manifest.Application.Version);
        string message;
        string title;
        MessageBoxImage image;

        if (!Version.TryParse(previousInstallation.Version, out var installedVersion))
        {
            title = "Update application";
            message = $"The installed version of {_manifest.Application.Name} cannot be "
                + $"identified. Replace it with version {_manifest.Application.Version}?";
            image = MessageBoxImage.Warning;
        }
        else if (packageVersion > installedVersion)
        {
            title = "Update application";
            message = $"Update {_manifest.Application.Name} from version "
                + $"{previousInstallation.Version} to {_manifest.Application.Version}?";
            image = MessageBoxImage.Question;
        }
        else if (packageVersion == installedVersion)
        {
            title = "Reinstall application";
            message = $"Version {_manifest.Application.Version} is already installed. "
                + "Reinstall it?";
            image = MessageBoxImage.Question;
        }
        else
        {
            title = "Install older version";
            message = $"Version {previousInstallation.Version} is currently installed. "
                + $"Install older version {_manifest.Application.Version} instead?";
            image = MessageBoxImage.Warning;
        }

        return MessageBox.Show(
            message,
            title,
            MessageBoxButton.YesNo,
            image) == MessageBoxResult.Yes;
    }

    /// <summary>
    /// Determines whether an external destination is empty, marked for this
    /// application, or already represented by the previous installed manifest.
    /// </summary>
    private bool IsCompatibleExternalDirectory(
        string directory,
        InstalledManifest? previousInstallation)
    {
        if (SafePaths.IsCompatibleInstallDirectory(
                directory,
                _manifest.Application.Id))
        {
            return true;
        }

        return previousInstallation is not null
            && previousInstallation.ApplicationId.Equals(
                _manifest.Application.Id,
                StringComparison.OrdinalIgnoreCase)
            && (previousInstallation.ExternalFiles.Any(
                    path => IsPathInsideDirectory(path, directory))
                || previousInstallation.ExternalDirectories.Any(
                    path => IsPathInsideDirectory(path, directory)));
    }

    /// <summary>
    /// Determines whether a path is equal to or contained by a directory.
    /// </summary>
    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.GetFullPath(
            Path.TrimEndingDirectorySeparator(directory));
        var directoryPrefix = fullDirectory + Path.DirectorySeparatorChar;

        return fullPath.Equals(fullDirectory, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(directoryPrefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares two normalized filesystem paths.
    /// </summary>
    private static bool PathsEqual(string left, string right) =>
        Path.GetFullPath(Path.TrimEndingDirectorySeparator(left)).Equals(
            Path.GetFullPath(Path.TrimEndingDirectorySeparator(right)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Checks that no file in the active installation is locked before an update.
    /// </summary>
    private static bool CanAcquireExclusiveAccess(
        string directory,
        out string? inaccessibleFile)
    {
        try
        {
            foreach (var relative in FileInventory.Enumerate(directory))
            {
                var file = Path.Combine(directory, relative);
                using var stream = new FileStream(
                    file,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None);
            }
        }
        catch (IOException exception)
        {
            inaccessibleFile = exception.Message;
            return false;
        }
        catch (UnauthorizedAccessException exception)
        {
            inaccessibleFile = exception.Message;
            return false;
        }
        catch (InvalidDataException exception)
        {
            inaccessibleFile = exception.Message;
            return false;
        }

        inaccessibleFile = null;
        return true;
    }

    /// <summary>
    /// Captures all values of the application's uninstall registry key.
    /// </summary>
    private static RegistrySnapshot CaptureRegistry(string uninstallKey)
    {
        using var key = Registry.CurrentUser.OpenSubKey(uninstallKey);

        if (key is null)
        {
            return new RegistrySnapshot(false, []);
        }

        var values = key.GetValueNames()
            .Select(name => new RegistryValueSnapshot(
                name,
                key.GetValue(
                    name,
                    null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames)!,
                key.GetValueKind(name)))
            .ToList();

        return new RegistrySnapshot(true, values);
    }

    /// <summary>
    /// Writes the Windows uninstall registration for the new version.
    /// </summary>
    private void WriteRegistry(string uninstallKey, string uninstaller)
    {
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
    }

    /// <summary>
    /// Restores the uninstall registry key to its state before installation.
    /// </summary>
    private static void RestoreRegistry(
        string uninstallKey,
        RegistrySnapshot snapshot)
    {
        Registry.CurrentUser.DeleteSubKeyTree(uninstallKey, false);

        if (!snapshot.Existed)
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(uninstallKey);

        foreach (var value in snapshot.Values)
        {
            key.SetValue(value.Name, value.Value, value.Kind);
        }
    }

    /// <summary>
    /// Restores every resource changed by an interrupted installation.
    /// </summary>
    private static bool RollbackInstallation(
        InstallationDirectoryTransaction? directoryTransaction,
        FileBackupTransaction fileBackups,
        IEnumerable<string> externalDirectories,
        string uninstallKey,
        RegistrySnapshot registrySnapshot,
        bool registryChanged)
    {
        var succeeded = true;

        void Attempt(Action action)
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                succeeded = false;
                InstallerLog.Error(exception);
            }
        }

        if (registryChanged)
        {
            Attempt(() => RestoreRegistry(uninstallKey, registrySnapshot));
        }

        Attempt(fileBackups.Rollback);

        foreach (var directory in externalDirectories
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderByDescending(directory => directory.Length))
        {
            Attempt(() =>
            {
                if (Directory.Exists(directory)
                    && !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            });
        }

        if (directoryTransaction is not null)
        {
            Attempt(directoryTransaction.Rollback);
        }

        return succeeded;
    }

    private sealed record RegistrySnapshot(
        bool Existed,
        IReadOnlyList<RegistryValueSnapshot> Values);

    private sealed record RegistryValueSnapshot(
        string Name,
        object Value,
        RegistryValueKind Kind);

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
