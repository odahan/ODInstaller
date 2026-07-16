using OdInstaller.Core;

namespace OdInstaller.Core.Tests;
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
}
