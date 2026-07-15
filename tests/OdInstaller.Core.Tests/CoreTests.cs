using OdInstaller.Core;

namespace OdInstaller.Core.Tests;
public sealed class CoreTests
{
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
