using OD.Installer.Core;

namespace OD.Installer.Builder.Tests;
public sealed class PackageTests
{
    [Fact]
    public void InstalledManifest_RoundTrips()
    {
        var path = Path.GetTempFileName();
        try { JsonFiles.WriteInstalledManifest(path, new InstalledManifest { ApplicationId = "Demo", Files = ["demo.exe"] }); var manifest = JsonFiles.ReadInstalledManifest(path); Assert.Equal("Demo", manifest.ApplicationId); Assert.Equal("demo.exe", Assert.Single(manifest.Files)); } finally { File.Delete(path); }
    }
}
