using System.Buffers.Binary;
using System.IO.Compression;
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

    [Fact]
    public void PackageFormat_AppendOpenExtract_RoundTripsAllEntries()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(temp, "app");
            Directory.CreateDirectory(Path.Combine(source, "nested"));
            File.WriteAllText(Path.Combine(source, "nested", "file.txt"), "content");

            var manifest = Path.Combine(temp, "installer.json");
            File.WriteAllText(manifest, "{}");
            var license = Path.Combine(temp, "LICENSE.txt");
            File.WriteAllText(license, "license text");
            var uninstaller = Path.Combine(temp, "uninstaller.exe");
            File.WriteAllText(uninstaller, "uninstaller bytes");

            var host = Path.Combine(temp, "host.exe");
            File.WriteAllText(host, "HOST");
            var output = Path.Combine(temp, "setup.exe");

            PackageFormat.Append(
                host, output, source, manifest, license, uninstaller, null, null);

            var extractDirectory = Path.Combine(temp, "extracted");
            Directory.CreateDirectory(extractDirectory);

            using (var payload = PackageFormat.OpenPayload(output))
            {
                FileInventory.ExtractSafely(payload, extractDirectory);
            }

            Assert.Equal(
                "content",
                File.ReadAllText(Path.Combine(extractDirectory, "app", "nested", "file.txt")));
            Assert.Equal(
                "license text",
                File.ReadAllText(Path.Combine(extractDirectory, "LICENSE.txt")));
            Assert.True(File.Exists(Path.Combine(extractDirectory, "installer.json")));
            Assert.True(File.Exists(
                Path.Combine(extractDirectory, "uninstaller", "OD.Installer.Uninstaller.exe")));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void PackageFormat_OpenPayload_RejectsPlainExecutable()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "this is not an installer package");
            Assert.Throws<InvalidDataException>(() => PackageFormat.OpenPayload(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void PackageFormat_OpenPayload_ReadsLegacyV1Footer()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var output = Path.Combine(temp, "legacy.exe");

            var zip = new MemoryStream();
            using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = archive.CreateEntry("installer.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("{}");
            }

            var zipBytes = zip.ToArray();
            using (var stream = new FileStream(output, FileMode.Create))
            {
                stream.Write("HOST"u8.ToArray());
                stream.Write(zipBytes);
                stream.Write("ODINST01"u8.ToArray());
                Span<byte> length = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(length, zipBytes.Length);
                stream.Write(length);
            }

            var extract = Path.Combine(temp, "extracted");
            Directory.CreateDirectory(extract);

            using (var payload = PackageFormat.OpenPayload(output))
            {
                FileInventory.ExtractSafely(payload, extract);
            }

            Assert.True(File.Exists(Path.Combine(extract, "installer.json")));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void PackageFormat_OpenPayload_ToleratesTrailingDataAfterFooter()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(temp, "app");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "file.txt"), "content");

            var manifest = Path.Combine(temp, "installer.json");
            File.WriteAllText(manifest, "{}");
            var license = Path.Combine(temp, "LICENSE.txt");
            File.WriteAllText(license, "license");
            var uninstaller = Path.Combine(temp, "uninstaller.exe");
            File.WriteAllText(uninstaller, "uninstaller");

            var host = Path.Combine(temp, "host.exe");
            File.WriteAllText(host, "HOST");
            var output = Path.Combine(temp, "setup.exe");
            PackageFormat.Append(host, output, source, manifest, license, uninstaller, null, null);

            var signature = new byte[512];
            Random.Shared.NextBytes(signature);
            using (var append = new FileStream(output, FileMode.Append))
            {
                append.Write(signature);
            }

            var extract = Path.Combine(temp, "extracted");
            Directory.CreateDirectory(extract);

            using (var payload = PackageFormat.OpenPayload(output))
            {
                FileInventory.ExtractSafely(payload, extract);
            }

            Assert.Equal(
                "content",
                File.ReadAllText(Path.Combine(extract, "app", "file.txt")));
        }
        finally { Directory.Delete(temp, true); }
    }

    [Fact]
    public void PackageFormat_OpenPayload_IgnoresFalseMarkerInTrailingData()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var source = Path.Combine(temp, "app");
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "file.txt"), "content");

            var manifest = Path.Combine(temp, "installer.json");
            File.WriteAllText(manifest, "{}");
            var license = Path.Combine(temp, "LICENSE.txt");
            File.WriteAllText(license, "license");
            var uninstaller = Path.Combine(temp, "uninstaller.exe");
            File.WriteAllText(uninstaller, "uninstaller");

            var host = Path.Combine(temp, "host.exe");
            File.WriteAllText(host, "HOST");
            var output = Path.Combine(temp, "setup.exe");
            PackageFormat.Append(host, output, source, manifest, license, uninstaller, null, null);

            using (var append = new FileStream(output, FileMode.Append))
            {
                append.Write("ODINST02"u8.ToArray());
                append.Write(new byte[12]);
                append.Write("tail"u8.ToArray());
            }

            var extract = Path.Combine(temp, "extracted");
            Directory.CreateDirectory(extract);

            using (var payload = PackageFormat.OpenPayload(output))
            {
                FileInventory.ExtractSafely(payload, extract);
            }

            Assert.Equal(
                "content",
                File.ReadAllText(Path.Combine(extract, "app", "file.txt")));
        }
        finally { Directory.Delete(temp, true); }
    }
}
