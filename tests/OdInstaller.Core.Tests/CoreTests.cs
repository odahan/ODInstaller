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
    public void Resolve_AllowsTheManifestDirectoryItself()
    {
        var root = Path.GetTempPath();
        Assert.Equal(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), ManifestValidator.Resolve(root, "."));
    }

    [Fact]
    public void ResolveOutput_AllowsAParentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "project");
        Assert.Equal(Path.Combine(Path.GetTempPath(), "output"), ManifestValidator.ResolveOutput(root, "../output"));
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
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "missing.exe" }, Source = new SourceManifest { Directory = "source" }, License = new LicenseManifest { File = "LICENSE.txt" }, Output = new OutputManifest { FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("executable", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ManifestValidator_ReportsInvalidWelcomeImage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "source")); File.WriteAllText(Path.Combine(root, "source", "test.exe"), "x"); File.WriteAllText(Path.Combine(root, "LICENSE.txt"), "x");
        try { var result = ManifestValidator.Validate(new InstallerManifest { Application = new ApplicationManifest { Id = "test", Name = "Test", Version = "1.0", Executable = "test.exe", WelcomeImage = "welcome.jpg" }, Source = new SourceManifest { Directory = "source" }, License = new LicenseManifest { File = "LICENSE.txt" }, Output = new OutputManifest { FileName = "test.exe" } }, root); Assert.Contains(result.Errors, x => x.Contains("welcomeImage", StringComparison.Ordinal)); } finally { Directory.Delete(root, true); }
    }
}
