using OD.Installer.Core;
using System.ComponentModel;
using System.IO;
using System.Windows;
using Forms = System.Windows.Forms;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using MessageBox = System.Windows.MessageBox;

namespace OD.Installer.Configurator;

// Code-behind for the main configuration window.
// Lets the user create, open, edit, validate and save an "installer.json" manifest.
public partial class MainWindow : Window
{
    private const int MaxRecentFiles = 3;
    private static readonly string RecentFilesStoragePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OD.Installer.Configurator",
        "recent-files.json");

    // Path of the currently loaded/saved manifest file, or null for a new, unsaved configuration.
    private string? currentPath;

    private List<RecentFile> recentFiles = [];

    // Indicates whether the form has unsaved changes.
    private bool isDirty;

    public MainWindow()
    {
        InitializeComponent();

        // Wire up standard application commands (New/Open/Save) to keyboard shortcuts.
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.New,
            (_, _) => NewConfiguration()));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Open,
            (_, _) => OpenConfiguration()));
        CommandBindings.Add(new System.Windows.Input.CommandBinding(
            System.Windows.Input.ApplicationCommands.Save,
            (_, _) => SaveConfiguration()));

        LoadRecentFiles();
        UpdateRecentFilesMenu();
        ResetForm();

        // Track any change made by the user in text fields or checkboxes to mark the form as dirty.
        AddHandler(
            System.Windows.Controls.TextBox.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) => MarkDirty()));
        AddHandler(
            System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
            new RoutedEventHandler((_, _) => MarkDirty()));
        AddHandler(
            System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
            new RoutedEventHandler((_, _) => MarkDirty()));
    }

    // Resets all form fields to their default values (used for "New configuration").
    private void ResetForm()
    {
        currentPath = null;

        ApplicationIdBox.Text = "";
        ApplicationNameBox.Text = "";
        VersionBox.Text = "1.0.0";
        PublisherBox.Text = "";
        ExecutableBox.Text = "";
        IconBox.Text = "";
        WelcomeImageBox.Text = "";

        SourceDirectoryBox.Text = "";
        InstallationDirectoryBox.Text = "{LocalAppData}/Programs/<Application>";
        AllowDirectorySelectionBox.IsChecked = true;
        LicenseFileBox.Text = "";
        RequireAcceptanceBox.IsChecked = true;

        StartMenuBox.IsChecked = true;
        DesktopBox.IsChecked = false;
        OutputDirectoryBox.Text = "";
        OutputFileNameBox.Text = "";

        isDirty = false;
        UpdateTitle();
    }

    // Builds an InstallerManifest instance from the current values of the form fields.
    private InstallerManifest GetManifest() => new()
    {
        Application = new ApplicationManifest
        {
            Id = ApplicationIdBox.Text.Trim(),
            Name = ApplicationNameBox.Text.Trim(),
            Version = VersionBox.Text.Trim(),
            Publisher = PublisherBox.Text.Trim(),
            Executable = ExecutableBox.Text.Trim(),
            Icon = EmptyToNull(IconBox.Text),
            WelcomeImage = EmptyToNull(WelcomeImageBox.Text)
        },
        Source = new SourceManifest
        {
            Directory = SourceDirectoryBox.Text.Trim()
        },
        Installation = new InstallationManifest
        {
            Scope = "perUser",
            Directory = InstallationDirectoryBox.Text.Trim(),
            AllowDirectorySelection = AllowDirectorySelectionBox.IsChecked == true
        },
        License = new LicenseManifest
        {
            File = LicenseFileBox.Text.Trim(),
            RequireAcceptance = RequireAcceptanceBox.IsChecked == true
        },
        Shortcuts = new ShortcutManifest
        {
            StartMenu = StartMenuBox.IsChecked == true,
            Desktop = DesktopBox.IsChecked == true
        },
        Output = new OutputManifest
        {
            Directory = OutputDirectoryBox.Text.Trim(),
            FileName = OutputFileNameBox.Text.Trim()
        }
    };

    // Converts a blank/whitespace-only string to null, otherwise returns the trimmed value.
    private static string? EmptyToNull(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Populates the form fields from an existing manifest (used when opening a file).
    private void SetManifest(InstallerManifest manifest)
    {
        ApplicationIdBox.Text = manifest.Application.Id;
        ApplicationNameBox.Text = manifest.Application.Name;
        VersionBox.Text = manifest.Application.Version;
        PublisherBox.Text = manifest.Application.Publisher;
        ExecutableBox.Text = manifest.Application.Executable;
        IconBox.Text = manifest.Application.Icon ?? "";
        WelcomeImageBox.Text = manifest.Application.WelcomeImage ?? "";

        SourceDirectoryBox.Text = manifest.Source.Directory;
        InstallationDirectoryBox.Text = manifest.Installation.Directory;
        AllowDirectorySelectionBox.IsChecked = manifest.Installation.AllowDirectorySelection;
        LicenseFileBox.Text = manifest.License.File;
        RequireAcceptanceBox.IsChecked = manifest.License.RequireAcceptance;

        StartMenuBox.IsChecked = manifest.Shortcuts.StartMenu;
        DesktopBox.IsChecked = manifest.Shortcuts.Desktop;
        OutputDirectoryBox.Text = manifest.Output.Directory;
        OutputFileNameBox.Text = manifest.Output.FileName;
    }

    // Menu/button click handlers delegating to the corresponding actions.
    private void New_Click(object sender, RoutedEventArgs e) => NewConfiguration();
    private void Open_Click(object sender, RoutedEventArgs e) => OpenConfiguration();
    private void Save_Click(object sender, RoutedEventArgs e) => SaveConfiguration();
    private void SaveAs_Click(object sender, RoutedEventArgs e) => SaveConfiguration(forceSaveAs: true);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    // Starts a new, empty configuration after confirming that unsaved changes can be discarded.
    private void NewConfiguration()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        ResetForm();
    }

    // Opens an existing manifest file selected by the user and loads it into the form.
    private void OpenConfiguration()
    {
        if (!ConfirmDiscardChanges())
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Ouvrir une configuration",
            Filter = "Configuration JSON (*.json)|*.json|Tous les fichiers (*.*)|*.*",
            InitialDirectory = GetLastConfigurationDirectory()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            OpenConfigurationFile(dialog.FileName);
        }
        catch (System.Text.Json.JsonException exception)
        {
            // Report JSON parsing errors with the line number when available.
            var line = exception.LineNumber is null ? "" : $" (ligne {exception.LineNumber.Value + 1})";
            MessageBox.Show(
                this,
                $"Le fichier sélectionné n'est pas un JSON de configuration valide{line}.\n\nLes commentaires et les virgules finales sont acceptés. Corrigez la syntaxe puis réessayez.",
                "Ouverture impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"Ce fichier ne peut pas être ouvert.\n\n{exception.Message}",
                "Ouverture impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Saves the current configuration to disk, prompting for a file path if needed.
    private void SaveConfiguration(bool forceSaveAs = false)
    {
        if (forceSaveAs || currentPath is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Enregistrer la configuration",
                Filter = "Configuration JSON (*.json)|*.json",
                FileName = currentPath is null ? "installer.json" : Path.GetFileName(currentPath),
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            currentPath = dialog.FileName;
        }

        try
        {
            File.WriteAllText(currentPath, System.Text.Json.JsonSerializer.Serialize(GetManifest(), JsonFiles.Options));
            AddRecentFile(currentPath);
            isDirty = false;
            UpdateTitle();
            StatusText.Text = "Configuration enregistrée.";
            MessageBox.Show(
                this,
                "La configuration a été enregistrée avec succès.",
                "Enregistrement réussi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"La configuration n'a pas pu être enregistrée.\n\n{exception.Message}",
                "Enregistrement impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // Validates the current configuration and displays either a success or an error dialog
    // listing every problem that needs to be fixed.
    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        var validation = ManifestValidator.Validate(
            GetManifest(),
            currentPath is null ? Environment.CurrentDirectory : Path.GetDirectoryName(currentPath)!);

        if (validation.IsValid)
        {
            StatusText.Text = "La configuration est valide et prête à être générée.";
            MessageBox.Show(this, "La configuration est valide.", "Vérification", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var errors = string.Join(Environment.NewLine, validation.Errors.Select(error => "• " + error));
        StatusText.Text = $"{validation.Errors.Count} problème(s) à corriger.";
        MessageBox.Show(
            this,
            $"La configuration contient les problèmes suivants :\n\n{errors}",
            "Vérification",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    // Browse button handlers: open the relevant file/folder picker for each field.
    private void BrowseSource_Click(object sender, RoutedEventArgs e) =>
        BrowseFolder(SourceDirectoryBox, "Choisir le dossier contenant l'application");

    private void BrowseOutput_Click(object sender, RoutedEventArgs e) =>
        BrowseFolder(OutputDirectoryBox, "Choisir le dossier de sortie");

    private void BrowseIcon_Click(object sender, RoutedEventArgs e) =>
        BrowseFile(IconBox, "Choisir une icône", "Icône (*.ico)|*.ico|Tous les fichiers (*.*)|*.*");

    private void BrowseWelcomeImage_Click(object sender, RoutedEventArgs e) =>
        BrowseFile(WelcomeImageBox, "Choisir une image d'accueil", "Image PNG (*.png)|*.png|Tous les fichiers (*.*)|*.*");

    private void BrowseLicense_Click(object sender, RoutedEventArgs e) =>
        BrowseFile(LicenseFileBox, "Choisir le fichier de licence", "Fichiers texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*");

    // Shows a folder browser dialog and writes the selected path into the target text box.
    private void BrowseFolder(System.Windows.Controls.TextBox target, string description)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            InitialDirectory = Directory.Exists(target.Text) ? target.Text : Environment.CurrentDirectory
        };

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            target.Text = dialog.SelectedPath;
            MarkDirty();
        }
    }

    // Shows a file open dialog and writes the selected path into the target text box.
    private void BrowseFile(System.Windows.Controls.TextBox target, string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };

        if (dialog.ShowDialog(this) == true)
        {
            target.Text = dialog.FileName;
            MarkDirty();
        }
    }

    private void RecentFilesMenu_SubmenuOpened(object sender, RoutedEventArgs e) => UpdateRecentFilesMenu();

    private void RecentFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.MenuItem { Tag: string path })
        {
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        if (!File.Exists(path))
        {
            recentFiles.RemoveAll(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
            SaveRecentFiles();
            UpdateRecentFilesMenu();
            MessageBox.Show(
                this,
                "Ce fichier récent est introuvable. Il a été retiré de la liste.",
                "Fichier introuvable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            OpenConfigurationFile(path);
        }
        catch (System.Text.Json.JsonException exception)
        {
            ShowOpenError(exception);
        }
        catch (Exception exception)
        {
            ShowOpenError(exception);
        }
    }

    private void OpenConfigurationFile(string path)
    {
        SetManifest(JsonFiles.ReadManifest(path));
        currentPath = path;
        AddRecentFile(path);
        isDirty = false;
        UpdateTitle();
        StatusText.Text = "Configuration ouverte.";
    }

    private void ShowOpenError(Exception exception)
    {
        if (exception is System.Text.Json.JsonException jsonException)
        {
            var line = jsonException.LineNumber is null ? "" : $" (ligne {jsonException.LineNumber.Value + 1})";
            MessageBox.Show(
                this,
                $"Le fichier sélectionné n'est pas un JSON de configuration valide{line}.\n\nLes commentaires et les virgules finales sont acceptés. Corrigez la syntaxe puis réessayez.",
                "Ouverture impossible",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        MessageBox.Show(
            this,
            $"Ce fichier ne peut pas être ouvert.\n\n{exception.Message}",
            "Ouverture impossible",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private string GetLastConfigurationDirectory()
    {
        var latestPath = recentFiles.FirstOrDefault()?.Path;
        var directory = latestPath is null ? null : Path.GetDirectoryName(latestPath);
        return directory is not null && Directory.Exists(directory) ? directory : Environment.CurrentDirectory;
    }

    private void LoadRecentFiles()
    {
        try
        {
            if (File.Exists(RecentFilesStoragePath))
            {
                recentFiles = System.Text.Json.JsonSerializer.Deserialize<List<RecentFile>>(File.ReadAllText(RecentFilesStoragePath)) ?? [];
            }

            recentFiles = recentFiles
                .Where(file => !string.IsNullOrWhiteSpace(file.Path))
                .OrderByDescending(file => file.LastUsed)
                .Take(MaxRecentFiles)
                .ToList();
        }
        catch (Exception)
        {
            recentFiles = [];
        }
    }

    private void AddRecentFile(string path)
    {
        recentFiles.RemoveAll(file => string.Equals(file.Path, path, StringComparison.OrdinalIgnoreCase));
        recentFiles.Insert(0, new RecentFile(path, DateTimeOffset.Now));
        recentFiles = recentFiles.Take(MaxRecentFiles).ToList();
        SaveRecentFiles();
        UpdateRecentFilesMenu();
    }

    private void SaveRecentFiles()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RecentFilesStoragePath)!);
            File.WriteAllText(RecentFilesStoragePath, System.Text.Json.JsonSerializer.Serialize(recentFiles));
        }
        catch (Exception)
        {
            // Recent files are a convenience feature and must not block the main workflow.
        }
    }

    private void UpdateRecentFilesMenu()
    {
        RecentFilesMenu.Items.Clear();

        if (recentFiles.Count == 0)
        {
            RecentFilesMenu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Aucun fichier récent", IsEnabled = false });
            return;
        }

        foreach (var file in recentFiles)
        {
            var menuItem = new System.Windows.Controls.MenuItem
            {
                Header = file.LastUsed.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                ToolTip = file.Path,
                Tag = file.Path
            };
            menuItem.Click += RecentFile_Click;
            RecentFilesMenu.Items.Add(menuItem);
        }
    }

    // Asks the user for confirmation before discarding unsaved changes.
    // Returns true if it is safe to proceed (no unsaved changes, or user confirmed).
    private bool ConfirmDiscardChanges() =>
        !isDirty || MessageBox.Show(
            this,
            "Les modifications non enregistrées seront perdues. Continuer ?",
            "Modifications non enregistrées",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    // Prevents the window from closing if there are unsaved changes the user did not confirm to discard.
    private void Window_Closing(object? sender, CancelEventArgs e) => e.Cancel = !ConfirmDiscardChanges();

    // Marks the form as having unsaved changes and refreshes the window title accordingly.
    private void MarkDirty()
    {
        isDirty = true;
        UpdateTitle();
    }

    // Updates the window title to reflect the dirty state and the current file name (if any).
    private void UpdateTitle()
    {
        Title = $"{(isDirty ? "* " : "")}OD Installer - Configuration{(currentPath is null ? "" : " - " + Path.GetFileName(currentPath))}";
    }

    private sealed record RecentFile(string Path, DateTimeOffset LastUsed);
}
