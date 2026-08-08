using OD.Installer.Core;

namespace OD.Installer.Core.Tests;
public sealed class CoreTests
{
    [Fact]
    public void ReadManifest_AcceptsCommentsAndTrailingCommas()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
              // A hand-edited configuration remains supported.
              "application": { "id": "Demo", "name": "Demo", "version": "1.0.0", "executable": "Demo.exe", },
              "source": { "directory": "C:\\source", },
              "installation": { "directory": "{LocalAppData}/Programs/Demo", },
              "license": { "file": "", "requireAcceptance": false, },
              "shortcuts": { },
              "output": { "directory": "C:\\output", "fileName": "Demo-Setup.exe", },
            }
            """);

            var manifest = JsonFiles.ReadManifest(path);

            Assert.Equal("Demo", manifest.Application.Name);
            Assert.Equal("Demo-Setup.exe", manifest.Output.FileName);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ReadManifest_RepairsLegacyDoubleQuotedFilePath()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
            {
              "application": {
                "id": "Demo",
                "name": "Demo",
                "version": "1.0.0",
                "executable": "Demo.exe",
                "icon": ""C:\legacy\Demo.ico""
              }
            }
            """);

            var manifest = JsonFiles.ReadManifest(path);

            Assert.Equal("C:\\legacy\\Demo.ico", manifest.Application.Icon);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SafePaths_RejectsTraversalAndAbsolutePaths()
    {
        Assert.False(SafePaths.TryResolveUnderRoot(Path.GetTempPath(), "../escape.txt", out _));
        Assert.False(SafePaths.IsSafeRelative("C:\\escape.txt"));
    }

    [Fact]
    public void ResolveInstallDirectory_ExpandsLocalAppData()
    {
        var result = SafePaths.ResolveInstallDirectory("{LocalAppData}/Programs/<Application>", "Demo");
        Assert.Contains("Programs", result); Assert.EndsWith("Demo", result);
    }

    [Fact]
    public void ResolveAbsolute_AcceptsFullyQualifiedPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "installer-input");
        Assert.Equal(Path.GetFullPath(path), ManifestValidator.ResolveAbsolute(path));
        Assert.Null(ManifestValidator.ResolveAbsolute("relative-path"));
    }

    [Fact]
    public void ResolveOutput_RequiresAnAbsoluteDirectory()
    {
        var output = Path.Combine(Path.GetTempPath(), "output");
        Assert.Equal(Path.GetFullPath(output), ManifestValidator.ResolveOutput(output));
        Assert.Throws<InvalidDataException>(() => ManifestValidator.ResolveOutput("../output"));
    }

    [Fact]
    public void ManifestValidator_ResolvesPathsRelativeToTheManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "publish")); File.WriteAllText(Path.Combine(root, "publish", "Demo.exe"), "x"); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try
        {
            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "demo", Name = "Demo", Version = "1.0", Executable = "Demo.exe" },
                Source = new SourceManifest { Directory = "./publish" },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Demo" },
                License = new LicenseManifest { File = "./LICENSE.txt", RequireAcceptance = true },
                Output = new OutputManifest { Directory = "..", FileName = "Demo-Setup.exe" }
            };

            Assert.True(ManifestValidator.Validate(manifest, root).IsValid);
            Assert.Equal(Path.GetFullPath(Path.Combine(root, "..")), ManifestValidator.ResolveOutput("..", root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Inventory_ReturnsRecursiveRelativeFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "nested")); File.WriteAllText(Path.Combine(root, "nested", "a.txt"), "x");
        try { Assert.Equal([Path.Combine("nested", "a.txt")], FileInventory.Enumerate(root)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_ReportsMissingExecutable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "missing.exe" }, Source = new SourceManifest { Directory = Path.Combine(root, "source") }, License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") }, Output = new OutputManifest { Directory = root, FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("executable", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_ReportsInvalidWelcomeImage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x"); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe", WelcomeImage = Path.Combine(root, "welcome.jpg") }, Source = new SourceManifest { Directory = Path.Combine(root, "source") }, License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") }, Output = new OutputManifest { Directory = root, FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("welcomeImage", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_ReportsInvalidIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x"); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe", Icon = Path.Combine(root, "icon.png") }, Source = new SourceManifest { Directory = Path.Combine(root, "source") }, License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") }, Output = new OutputManifest { Directory = root, FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("application.icon", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_RejectsApplicationNameWithInvalidCharacters()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x"); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Bad:Name", Version = "1.0", Executable = "test.exe" }, Source = new SourceManifest { Directory = Path.Combine(root, "source") }, License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") }, Output = new OutputManifest { Directory = root, FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("application.name", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_RequiresLicenseFileEvenWhenAcceptanceIsOptional()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe" }, Source = new SourceManifest { Directory = Path.Combine(root, "source") }, License = new LicenseManifest { File = "", RequireAcceptance = false }, Output = new OutputManifest { Directory = root, FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("license", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Inventory_RejectsJunctionDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            Directory.CreateDirectory(Path.Combine(root, "outside"));
            File.WriteAllText(Path.Combine(root, "outside", "secret.txt"), "x");

            var junction = Path.Combine(root, "source", "link");
            using var process = System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(
                    "cmd.exe",
                    $"/c mklink /J \"{junction}\" \"{Path.Combine(root, "outside")}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                })!;

            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            Assert.Throws<InvalidDataException>(
                () => FileInventory.Enumerate(Path.Combine(root, "source")));
        }
        finally
        {
            var junction = Path.Combine(root, "source", "link");
            if (Directory.Exists(junction)) Directory.Delete(junction);
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsCompatibleInstallDirectory_AcceptsMissingOrEmpty()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Assert.True(SafePaths.IsCompatibleInstallDirectory(Path.Combine(root, "missing"), "Demo"));
            Directory.CreateDirectory(root);
            Assert.True(SafePaths.IsCompatibleInstallDirectory(root, "Demo"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IsCompatibleInstallDirectory_RefusesForeignContent()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "unrelated.txt"), "x");
            Assert.False(SafePaths.IsCompatibleInstallDirectory(root, "Demo"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IsCompatibleInstallDirectory_AcceptsMatchingInstallationOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            JsonFiles.WriteInstalledManifest(
                Path.Combine(root, ".od.installer-installed.json"),
                new InstalledManifest
                {
                    ApplicationId = "Demo",
                    Version = "1.0",
                    InstallDirectory = root,
                    Files = [],
                    Directories = [],
                    Shortcuts = [],
                    RegistryKeys = []
                });
            File.WriteAllText(Path.Combine(root, "Demo.exe"), "x");

            Assert.True(SafePaths.IsCompatibleInstallDirectory(root, "Demo"));
            Assert.False(SafePaths.IsCompatibleInstallDirectory(root, "Other"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IsCompatibleInstallDirectory_RejectsCorruptManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, ".od.installer-installed.json"), "not json");
            Assert.False(SafePaths.IsCompatibleInstallDirectory(root, "Demo"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void IsSafeInstallTemplate_AcceptsPlaceholdersAndRejectsTraversal()
    {
        Assert.True(SafePaths.IsSafeInstallTemplate("{LocalAppData}/Programs/<Application>"));
        Assert.True(SafePaths.IsSafeInstallTemplate(Path.Combine(Path.GetTempPath(), "Demo")));
        Assert.False(SafePaths.IsSafeInstallTemplate("..\\Programs\\Demo"));
        Assert.False(SafePaths.IsSafeInstallTemplate("{LocalAppData}\\..\\Demo"));
        Assert.False(SafePaths.IsSafeInstallTemplate(""));
        Assert.False(SafePaths.IsSafeInstallTemplate("relative"));
    }

    [Fact]
    public void ManifestValidator_RejectsUnsafeInstallDirectoryTemplate()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "source"));
            File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe" },
                Source = new SourceManifest { Directory = Path.Combine(root, "source") },
                Installation = new InstallationManifest { Directory = "..\\Programs\\Test" },
                License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") },
                Output = new OutputManifest { Directory = root, FileName = "test.exe" }
            };

            var result = ManifestValidator.Validate(manifest, root);
            Assert.Contains(result.Errors, x => x.Contains("installation.directory", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ValidatePackage_AcceptsAValidPackage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "LICENSE.txt"), "license");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "demo", Name = "Demo", Version = "1.0", Executable = "Demo.exe" },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Demo" },
                License = new LicenseManifest { File = "ignored", RequireAcceptance = true }
            };

            Assert.True(ManifestValidator.ValidatePackage(manifest, payload).IsValid);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ValidatePackage_RejectsMissingLicenseAndUnsafeDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "demo", Name = "Demo", Version = "1.0", Executable = "Demo.exe" },
                Installation = new InstallationManifest { Directory = "..\\evil" },
                License = new LicenseManifest { File = "ignored" }
            };

            var result = ManifestValidator.ValidatePackage(manifest, payload);
            Assert.Contains(result.Errors, x => x.Contains("installation.directory", StringComparison.Ordinal));
            Assert.Contains(result.Errors, x => x.Contains("license", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ValidatePackage_RejectsMissingPackagedIcon()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var payload = Path.Combine(root, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(payload, "LICENSE.txt"), "license");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "demo", Name = "Demo", Version = "1.0", Executable = "Demo.exe", Icon = "icon.ico" },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Demo" },
                License = new LicenseManifest { File = "ignored" }
            };

            var result = ManifestValidator.ValidatePackage(manifest, payload);
            Assert.Contains(result.Errors, x => x.Contains("application.icon", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopySafely_CopiesRecursivelyAndTracksCreatedDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "b.txt"), "b");

            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(destination);
            var existing = Path.Combine(destination, "existing");
            Directory.CreateDirectory(existing);
            File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");

            var result = FileInventory.CopySafely(source, destination);

            Assert.Equal(["b.txt", Path.Combine("nested", "a.txt")], result.Files);
            Assert.Equal("a", File.ReadAllText(Path.Combine(destination, "nested", "a.txt")));
            Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
            Assert.Contains(Path.GetFullPath(Path.Combine(destination, "nested")), result.CreatedDirectories);
            Assert.DoesNotContain(Path.GetFullPath(existing), result.CreatedDirectories);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopySafely_ThrowsWhenCancelled()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");

            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(destination);

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(
                () => FileInventory.CopySafely(source, destination, cancellationToken: cts.Token));
            Assert.False(File.Exists(Path.Combine(destination, "a.txt")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CopySafely_ReportsProgress()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(root, "source");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            File.WriteAllText(Path.Combine(source, "b.txt"), "b");
            File.WriteAllText(Path.Combine(source, "c.txt"), "c");

            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(destination);

            var progress = new CollectingProgress<FileCopyProgress>();
            var result = FileInventory.CopySafely(source, destination, progress);

            Assert.Equal(3, progress.Values.Count);
            Assert.Equal(result.Files, progress.Values.Select(v => v.RelativePath));
            Assert.Equal(1, progress.Values[0].Current);
            Assert.Equal(3, progress.Values[^1].Current);
            Assert.All(progress.Values, v => Assert.Equal(3, v.Total));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SafePaths_IsSafeDestination_AcceptsRootRelativeAndAbsolute()
    {
        Assert.True(SafePaths.IsSafeDestination(""));
        Assert.True(SafePaths.IsSafeDestination("."));
        Assert.True(SafePaths.IsSafeDestination("plugins"));
        Assert.True(SafePaths.IsSafeDestination("plugins/nested"));
        Assert.True(SafePaths.IsSafeDestination(Path.Combine(Path.GetTempPath(), "Assets")));
        Assert.True(SafePaths.IsSafeDestination("{LocalAppData}/Programs/Assets"));
        Assert.False(SafePaths.IsSafeDestination("..\\plugins"));
        Assert.False(SafePaths.IsSafeDestination(".\\plugins"));
        Assert.False(SafePaths.IsSafeDestination(Path.Combine(Path.GetTempPath(), "..", "Assets")));
        Assert.False(SafePaths.IsSafeDestination("relative\\..\\Assets"));
        Assert.False(SafePaths.IsSafeDestination("\\server\\share"));
    }

    [Fact]
    public void SafePaths_IsExternalDestination_ClassifiesDestinations()
    {
        Assert.False(SafePaths.IsExternalDestination(""));
        Assert.False(SafePaths.IsExternalDestination("."));
        Assert.False(SafePaths.IsExternalDestination("plugins"));
        Assert.True(SafePaths.IsExternalDestination(Path.Combine(Path.GetTempPath(), "Assets")));
        Assert.True(SafePaths.IsExternalDestination("{LocalAppData}/Programs/Assets"));
    }

    [Fact]
    public void ManifestValidator_AcceptsMultipleSourceFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var main = Path.Combine(root, "main");
            var plugins = Path.Combine(root, "plugins-src");
            Directory.CreateDirectory(Path.Combine(main, "sub"));
            Directory.CreateDirectory(plugins);
            File.WriteAllText(Path.Combine(main, "sub", "Test.exe"), "x");
            File.WriteAllText(Path.Combine(plugins, "plug.txt"), "x");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = Path.Combine("sub", "Test.exe") },
                Source = new SourceManifest
                {
                    Folders =
                    [
                        new SourceFolderManifest { Directory = main, Destination = "." },
                        new SourceFolderManifest { Directory = plugins, Destination = "plugins" }
                    ]
                },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Test" },
                License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") },
                Output = new OutputManifest { Directory = root, FileName = "test.exe" }
            };

            Assert.True(ManifestValidator.Validate(manifest, root).IsValid);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_RejectsUnsafeFolderDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var main = Path.Combine(root, "main");
            Directory.CreateDirectory(main);
            File.WriteAllText(Path.Combine(main, "test.exe"), "x");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe" },
                Source = new SourceManifest
                {
                    Folders =
                    [
                        new SourceFolderManifest { Directory = main, Destination = "." },
                        new SourceFolderManifest { Directory = main, Destination = "..\\plugins" }
                    ]
                },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Test" },
                License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") },
                Output = new OutputManifest { Directory = root, FileName = "test.exe" }
            };

            var result = ManifestValidator.Validate(manifest, root);
            Assert.Contains(result.Errors, x => x.Contains("destination", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_RejectsCollidingSourceFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var main = Path.Combine(root, "main");
            var plugins = Path.Combine(root, "plugins-src");
            Directory.CreateDirectory(Path.Combine(main, "plugins"));
            Directory.CreateDirectory(plugins);
            File.WriteAllText(Path.Combine(main, "test.exe"), "x");
            File.WriteAllText(Path.Combine(main, "plugins", "plug.txt"), "x");
            File.WriteAllText(Path.Combine(plugins, "plug.txt"), "x");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe" },
                Source = new SourceManifest
                {
                    Folders =
                    [
                        new SourceFolderManifest { Directory = main, Destination = "." },
                        new SourceFolderManifest { Directory = plugins, Destination = "plugins" }
                    ]
                },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Test" },
                License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") },
                Output = new OutputManifest { Directory = root, FileName = "test.exe" }
            };

            var result = ManifestValidator.Validate(manifest, root);
            Assert.Contains(result.Errors, x => x.Contains("collision", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_RequiresAFolderMappedToTheRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var plugins = Path.Combine(root, "plugins-src");
            Directory.CreateDirectory(plugins);
            File.WriteAllText(Path.Combine(plugins, "plug.txt"), "x");
            File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");

            var manifest = new InstallerManifest
            {
                Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "plug.txt" },
                Source = new SourceManifest
                {
                    Folders = [new SourceFolderManifest { Directory = plugins, Destination = "plugins" }]
                },
                Installation = new InstallationManifest { Directory = "{LocalAppData}/Programs/Test" },
                License = new LicenseManifest { File = Path.Combine(root, "LICENSE.txt") },
                Output = new OutputManifest { Directory = root, FileName = "test.exe" }
            };

            var result = ManifestValidator.Validate(manifest, root);
            Assert.Contains(result.Errors, x => x.Contains("installation root", StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CollectingProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];
        public void Report(T value) => Values.Add(value);
    }
}
