using System.Reflection;
using System.Text.Json;
using OD.Installer.Core;

namespace OD.Installer.Builder;

/// <summary>
/// Provides the command-line entry point for building installer packages.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Executes the requested builder command.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments. The expected format is:
    /// <c>build &lt;installer.json&gt; [--output &lt;directory&gt;] [--force] [--verbose]</c>.
    /// </param>
    /// <returns>
    /// Zero when the package is successfully created; otherwise, a non-zero exit code.
    /// </returns>
    private static int Main(string[] args)
    {
        CliArguments parsed;

        try
        {
            parsed = CliParser.Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return Usage();
        }

        if (parsed.ShowHelp)
        {
            return Help();
        }

        if (parsed.ShowVersion)
        {
            Console.WriteLine(VersionString);
            return 0;
        }

        try
        {
            var manifestPath = Path.GetFullPath(parsed.ManifestPath);

            // Resolve all relative paths from the manifest directory.
            var root = Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidDataException(
                    "Manifest path has no directory.");

            var manifest = JsonFiles.ReadManifest(manifestPath);
            var validation = ManifestValidator.Validate(manifest, root);

            if (!validation.IsValid)
            {
                foreach (var error in validation.Errors)
                {
                    Console.Error.WriteLine($"error: {error}");
                }

                return 2;
            }

            var outputDirectory =
                parsed.Output
                ?? ManifestValidator.ResolveOutput(
                    manifest.Output.Directory,
                    root);

            var outputPath = Path.Combine(
                outputDirectory,
                manifest.Output.FileName);

            if (File.Exists(outputPath) && !parsed.Force)
            {
                throw new IOException(
                    $"Output exists: {outputPath}. "
                    + "Use --force to replace it.");
            }

            // The runtime executables are embedded into the generated package.
            var runtimeDirectory = Path.Combine(
                AppContext.BaseDirectory,
                "runtime");

            var setupPath = Path.Combine(
                runtimeDirectory,
                "OD.Installer.Setup.exe");

            var uninstallerPath = Path.Combine(
                runtimeDirectory,
                "OD.Installer.Uninstaller.exe");

            if (!File.Exists(setupPath)
                || !File.Exists(uninstallerPath))
            {
                throw new FileNotFoundException(
                    "Installer runtime is missing. "
                    + "Publish the runtime with build-runtime.ps1 "
                    + "before building packages.");
            }

            var sourcePath = ManifestValidator.ResolveManifestPath(
                manifest.Source.Directory,
                root)!;

            var licensePath = ManifestValidator.ResolveManifestPath(
                manifest.License.File,
                root)!;

            var appFiles = FileInventory.Enumerate(sourcePath);

            if (parsed.Verbose)
            {
                Console.WriteLine($"Manifest: {manifestPath}");
                Console.WriteLine($"Output: {outputPath}");
                Console.WriteLine($"Source: {sourcePath}");
                Console.WriteLine($"License: {licensePath}");
                Console.WriteLine($"Setup host: {setupPath}");
                Console.WriteLine($"Uninstaller: {uninstallerPath}");
            }

            Console.WriteLine(
                $"Including {appFiles.Count} application files.");

            if (parsed.Verbose)
            {
                foreach (var file in appFiles)
                {
                    Console.WriteLine($"  {file}");
                }
            }

            // Store a normalized copy of the manifest in the temporary directory.
            var normalizedManifestPath = Path.Combine(
                Path.GetTempPath(),
                $"od.installer-{Guid.NewGuid():N}.json");

            try
            {
                File.WriteAllText(
                    normalizedManifestPath,
                    JsonSerializer.Serialize(
                        manifest,
                        JsonFiles.Options));

                var welcomeImagePath =
                    string.IsNullOrWhiteSpace(
                        manifest.Application.WelcomeImage)
                        ? null
                        : ManifestValidator.ResolveManifestPath(
                            manifest.Application.WelcomeImage,
                            root);

                var iconPath =
                    string.IsNullOrWhiteSpace(
                        manifest.Application.Icon)
                        ? null
                        : ManifestValidator.ResolveManifestPath(
                            manifest.Application.Icon,
                            root);

                PackageFormat.Append(
                    setupPath,
                    outputPath,
                    sourcePath,
                    normalizedManifestPath,
                    licensePath,
                    uninstallerPath,
                    welcomeImagePath,
                    iconPath);
            }
            finally
            {
                // Always remove the temporary normalized manifest.
                File.Delete(normalizedManifestPath);
            }

            Console.WriteLine($"Created {outputPath}");

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");

            return 1;
        }
    }

    /// <summary>
    /// The builder's informational version string.
    /// </summary>
    private static string VersionString =>
        typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? "unknown";

    /// <summary>
    /// Displays the supported command-line syntax.
    /// </summary>
    /// <returns>The command-line usage error exit code.</returns>
    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: od.installer build <installer.json> "
            + "[--output <directory>] [--force] [--verbose]");
        Console.Error.WriteLine(
            "       od.installer --help | --version");

        return 2;
    }

    /// <summary>
    /// Displays the full help text.
    /// </summary>
    /// <returns>The success exit code.</returns>
    private static int Help()
    {
        Console.WriteLine("od.installer - build self-contained Windows installers.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  od.installer build <installer.json> [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --output <directory>  Override the output directory.");
        Console.WriteLine("  --force               Overwrite an existing installer file.");
        Console.WriteLine("  --verbose             Print detailed build information.");
        Console.WriteLine("  --help, -h            Show this help.");
        Console.WriteLine("  --version             Show the builder version.");

        return 0;
    }
}
