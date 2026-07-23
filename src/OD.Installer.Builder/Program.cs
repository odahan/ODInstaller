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
    /// <c>build &lt;installer.json&gt; [--output &lt;directory&gt;] [--force]</c>.
    /// </param>
    /// <returns>
    /// Zero when the package is successfully created; otherwise, a non-zero exit code.
    /// </returns>
    private static int Main(string[] args)
    {
        // The builder currently supports only the "build" command.
        if (args.Length < 2
            || !string.Equals(
                args[0],
                "build",
                StringComparison.OrdinalIgnoreCase))
        {
            return Usage();
        }

        try
        {
            var manifestPath = Path.GetFullPath(args[1]);

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
                Option(args, "--output")
                ?? ManifestValidator.ResolveOutput(
                    manifest.Output.Directory,
                    root);

            var outputPath = Path.Combine(
                outputDirectory,
                manifest.Output.FileName);

            var forceOverwrite = args.Contains(
                "--force",
                StringComparer.OrdinalIgnoreCase);

            if (File.Exists(outputPath) && !forceOverwrite)
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

            var fileCount = FileInventory.Enumerate(sourcePath).Count;

            Console.WriteLine(
                $"Including {fileCount} application files.");

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
    /// Retrieves the value following a command-line option.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="name">Option name to find.</param>
    /// <returns>
    /// The option value, or <see langword="null"/> when the option is missing.
    /// </returns>
    private static string? Option(
        string[] args,
        string name)
    {
        var index = Array.FindIndex(
            args,
            argument => string.Equals(
                argument,
                name,
                StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length
            ? args[index + 1]
            : null;
    }

    /// <summary>
    /// Displays the supported command-line syntax.
    /// </summary>
    /// <returns>The command-line usage error exit code.</returns>
    private static int Usage()
    {
        Console.Error.WriteLine(
            "Usage: od.installer build <installer.json> "
            + "[--output <directory>] [--force] [--verbose]");

        return 2;
    }
}
