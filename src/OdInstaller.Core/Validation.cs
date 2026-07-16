namespace OdInstaller.Core;

/// <summary>
/// Result of a manifest validation, containing the list of errors found.
/// </summary>
public sealed record ValidationResult(IReadOnlyList<string> Errors)
{
    /// <summary>
    /// Indicates whether the validated manifest has no errors.
    /// </summary>
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validates an <see cref="InstallerManifest"/> and resolves the paths it references.
/// </summary>
public static class ManifestValidator
{
    /// <summary>
    /// Validates an installer manifest against the manifest's directory.
    /// </summary>
    /// <param name="manifest">Manifest to validate.</param>
    /// <param name="manifestDirectory">Directory containing the manifest file, used to resolve relative paths.</param>
    /// <returns>The validation result, listing every error found.</returns>
    public static ValidationResult Validate(InstallerManifest manifest, string manifestDirectory)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(manifest.Application.Id)
            || manifest.Application.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add("application.id is required and must be a valid key.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Application.Name))
        {
            errors.Add("application.name is required.");
        }

        if (!Version.TryParse(manifest.Application.Version, out _))
        {
            errors.Add("application.version must be a valid version.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Application.Executable)
            || !SafePaths.IsSafeRelative(manifest.Application.Executable))
        {
            errors.Add("application.executable must be a safe relative path.");
        }

        if (!string.Equals(manifest.Installation.Scope, "perUser", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("installation.scope must be 'perUser' in V1.");
        }

        if (ResolveManifestPath(manifest.Output.Directory, manifestDirectory) is null)
        {
            errors.Add("output.directory must be a valid path.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Output.FileName)
            || Path.GetFileName(manifest.Output.FileName) != manifest.Output.FileName)
        {
            errors.Add("output.fileName must be a file name only.");
        }

        var source = ResolveManifestPath(manifest.Source.Directory, manifestDirectory);

        if (source is null || !Directory.Exists(source))
        {
            errors.Add("source.directory must be an existing directory.");
        }
        else if (!File.Exists(Path.Combine(source, manifest.Application.Executable)))
        {
            errors.Add("application.executable is missing from source.directory.");
        }

        if (manifest.License.RequireAcceptance
            && (ResolveManifestPath(manifest.License.File, manifestDirectory) is not string license
                || !File.Exists(license)))
        {
            errors.Add("license.file must be an existing file when acceptance is required.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Application.WelcomeImage))
        {
            var image = ResolveManifestPath(manifest.Application.WelcomeImage, manifestDirectory);

            if (image is null
                || !File.Exists(image)
                || !string.Equals(Path.GetExtension(image), ".png", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("application.welcomeImage must reference an existing PNG file.");
            }
        }

        if (!string.IsNullOrWhiteSpace(manifest.Application.Icon))
        {
            var icon = ResolveManifestPath(manifest.Application.Icon, manifestDirectory);

            if (icon is null
                || !File.Exists(icon)
                || !string.Equals(Path.GetExtension(icon), ".ico", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("application.icon must reference an existing ICO file.");
            }
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// Resolves an absolute path, returning <c>null</c> when the path is not fully qualified.
    /// </summary>
    public static string? ResolveAbsolute(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path)
            : null;

    /// <summary>
    /// Resolves a path relative to the manifest directory, or as an absolute path when already fully qualified.
    /// </summary>
    /// <param name="path">Path to resolve.</param>
    /// <param name="manifestDirectory">Directory containing the manifest file.</param>
    /// <returns>The resolved absolute path, or <c>null</c> when it cannot be resolved.</returns>
    public static string? ResolveManifestPath(string path, string manifestDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(manifestDirectory))
        {
            return null;
        }

        try
        {
            return Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(manifestDirectory, path));
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves the output directory as an absolute path.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when the directory is not an absolute path.</exception>
    public static string ResolveOutput(string directory) =>
        ResolveAbsolute(directory)
        ?? throw new InvalidDataException("output.directory must be an absolute path.");

    /// <summary>
    /// Resolves the output directory relative to the manifest directory.
    /// </summary>
    /// <exception cref="InvalidDataException">Thrown when the directory cannot be resolved.</exception>
    public static string ResolveOutput(string directory, string manifestDirectory) =>
        ResolveManifestPath(directory, manifestDirectory)
        ?? throw new InvalidDataException("output.directory must be a valid path.");
}

/// <summary>
/// Provides helpers to validate and safely resolve file system paths.
/// </summary>
public static class SafePaths
{
    /// <summary>
    /// Indicates whether a relative path is safe: not rooted, without
    /// parent/current directory segments, and without invalid file name characters.
    /// </summary>
    public static bool IsSafeRelative(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !Path.IsPathRooted(path)
        && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(x => x is ".." or ".")
        && Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>
    /// Attempts to resolve a relative path under a root directory, guarding
    /// against path traversal outside of that root.
    /// </summary>
    /// <param name="root">Root directory the path must stay under.</param>
    /// <param name="relative">Relative path to resolve.</param>
    /// <param name="fullPath">The resolved full path, when successful.</param>
    /// <returns><c>true</c> when the path is safely resolved under the root; otherwise <c>false</c>.</returns>
    public static bool TryResolveUnderRoot(string root, string relative, out string fullPath)
    {
        fullPath = "";

        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
        {
            return false;
        }

        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var rootFull = rootPath + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));

        if (!string.Equals(candidate, rootPath, StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// Resolves the install directory template by substituting known placeholders
    /// (<c>{LocalAppData}</c> and <c>&lt;Application&gt;</c>).
    /// </summary>
    /// <param name="template">Install directory template.</param>
    /// <param name="applicationName">Application name to substitute in the template.</param>
    public static string ResolveInstallDirectory(string template, string applicationName) =>
        template
            .Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
            .Replace("<Application>", applicationName, StringComparison.OrdinalIgnoreCase);
}
