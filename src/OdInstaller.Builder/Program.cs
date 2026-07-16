using OdInstaller.Core;

namespace OdInstaller.Builder;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2 || !string.Equals(args[0], "build", StringComparison.OrdinalIgnoreCase)) return Usage();
        try
        {
            var manifestPath = Path.GetFullPath(args[1]);
            var root = Path.GetDirectoryName(manifestPath) ?? throw new InvalidDataException("Manifest path has no directory.");
            var manifest = JsonFiles.ReadManifest(manifestPath);
            var validation = ManifestValidator.Validate(manifest, root);
            if (!validation.IsValid) { foreach (var error in validation.Errors) Console.Error.WriteLine("error: " + error); return 2; }
            var outputDirectory = Option(args, "--output") ?? ManifestValidator.ResolveOutput(manifest.Output.Directory, root);
            var output = Path.Combine(outputDirectory, manifest.Output.FileName);
            if (File.Exists(output) && !args.Contains("--force", StringComparer.OrdinalIgnoreCase)) throw new IOException($"Output exists: {output}. Use --force to replace it.");
            var runtime = Path.Combine(AppContext.BaseDirectory, "runtime");
            var setup = Path.Combine(runtime, "OdInstaller.Setup.exe"); var uninstaller = Path.Combine(runtime, "OdInstaller.Uninstaller.exe");
            if (!File.Exists(setup) || !File.Exists(uninstaller)) throw new FileNotFoundException("Installer runtime is missing. Publish the runtime with build-runtime.ps1 before building packages.");
            var source = ManifestValidator.ResolveManifestPath(manifest.Source.Directory, root)!; var license = ManifestValidator.ResolveManifestPath(manifest.License.File, root)!;
            Console.WriteLine($"Including {FileInventory.Enumerate(source).Count} application files.");
            var normalized = Path.Combine(Path.GetTempPath(), $"odinstaller-{Guid.NewGuid():N}.json");
            try { File.WriteAllText(normalized, System.Text.Json.JsonSerializer.Serialize(manifest, JsonFiles.Options)); PackageFormat.Append(setup, output, source, normalized, license, uninstaller, string.IsNullOrWhiteSpace(manifest.Application.WelcomeImage) ? null : ManifestValidator.ResolveManifestPath(manifest.Application.WelcomeImage, root), string.IsNullOrWhiteSpace(manifest.Application.Icon) ? null : ManifestValidator.ResolveManifestPath(manifest.Application.Icon, root)); }
            finally { File.Delete(normalized); }
            Console.WriteLine($"Created {output}"); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine("error: " + exception.Message); return 1; }
    }
    private static string? Option(string[] args, string name) { var index = Array.FindIndex(args, x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    private static int Usage() { Console.Error.WriteLine("Usage: odinstaller build <installer.json> [--output <directory>] [--force] [--verbose]"); return 2; }
}
