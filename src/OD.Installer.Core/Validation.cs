namespace OD.Installer.Core;

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
        ValidateCommon(manifest, errors);

        if (ResolveManifestPath(manifest.Output.Directory, manifestDirectory) is null)
        {
            errors.Add("output.directory must be a valid path.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Output.FileName)
            || Path.GetFileName(manifest.Output.FileName) != manifest.Output.FileName)
        {
            errors.Add("output.fileName must be a file name only.");
        }

        ValidateSourceFolders(manifest, manifestDirectory, errors);

        // The license file is always packaged as LICENSE.txt and presented in
        // the wizard, so it must exist even when acceptance is not mandatory.
        if (ResolveManifestPath(manifest.License.File, manifestDirectory) is not string license
            || !File.Exists(license))
        {
            errors.Add("license.file must be an existing file.");
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
    /// Validates a manifest loaded from an installer package against the
    /// packaged content. Used by Setup as a defense-in-depth check: the
    /// manifest must be well formed and the files it depends on must be
    /// present inside the extracted package. Paths that are only meaningful
    /// on the build machine (source directory, output) are not checked.
    /// </summary>
    /// <param name="manifest">Manifest extracted from the package.</param>
    /// <param name="payloadDirectory">Directory containing the extracted package.</param>
    /// <returns>The validation result, listing every error found.</returns>
    public static ValidationResult ValidatePackage(InstallerManifest manifest, string payloadDirectory)
    {
        var errors = new List<string>();
        ValidateCommon(manifest, errors);

        // The destinations are resolved on the target machine, so each folder
        // destination must still be safe even though the source directories
        // only exist on the build machine.
        foreach (var (folder, index) in manifest.Source.EffectiveFolders()
            .Select((folder, index) => (folder, index)))
        {
            if (!SafePaths.IsSafeDestination(folder.Destination))
            {
                errors.Add(
                    $"source.folders[{index}].destination must be empty, '.', "
                    + "a safe relative subfolder, or an absolute path.");
            }
        }

        // The license is always packaged as LICENSE.txt and displayed in the
        // wizard, so its presence is mandatory.
        if (!File.Exists(Path.Combine(payloadDirectory, "LICENSE.txt")))
        {
            errors.Add("license.file must be present in the package as LICENSE.txt.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Application.WelcomeImage)
            && !File.Exists(Path.Combine(payloadDirectory, "welcome.png")))
        {
            errors.Add("application.welcomeImage is missing from the package.");
        }

        if (!string.IsNullOrWhiteSpace(manifest.Application.Icon)
            && !File.Exists(Path.Combine(payloadDirectory, "application.ico")))
        {
            errors.Add("application.icon is missing from the package.");
        }

        return new ValidationResult(errors);
    }

    /// <summary>
    /// Validates every source folder: its directory must exist, its
    /// destination must be safe, and the files it maps into the installation
    /// root must not collide with files from other folders. At least one
    /// folder must be mapped to the installation root and contain the
    /// application executable.
    /// </summary>
    private static void ValidateSourceFolders(
        InstallerManifest manifest,
        string manifestDirectory,
        List<string> errors)
    {
        var folders = manifest.Source.EffectiveFolders();
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (folder, index) in folders.Select((folder, index) => (folder, index)))
        {
            var source = ResolveManifestPath(folder.Directory, manifestDirectory);

            if (source is null || !Directory.Exists(source))
            {
                errors.Add($"source.folders[{index}].directory must be an existing directory.");
                continue;
            }

            if (!SafePaths.IsSafeDestination(folder.Destination))
            {
                errors.Add(
                    $"source.folders[{index}].destination must be empty, '.', "
                    + "a safe relative subfolder, or an absolute path.");
            }

            // Absolute destinations are stored in their own package bucket,
            // so they cannot collide with the installation-root tree.
            if (SafePaths.IsExternalDestination(folder.Destination))
            {
                continue;
            }

            try
            {
                foreach (var file in FileInventory.Enumerate(source))
                {
                    var target = SafePaths.IsRootDestination(folder.Destination)
                        ? file
                        : Path.Combine(folder.Destination, file);

                    // Normalize the separators so "/" and "\" mixes compare equal.
                    target = target.Replace(
                        Path.AltDirectorySeparatorChar,
                        Path.DirectorySeparatorChar);

                    if (!targets.Add(target))
                    {
                        errors.Add($"File collision between source folders: {target}");
                    }
                }
            }
            catch (InvalidDataException exception)
            {
                errors.Add(exception.Message);
            }
        }

        var rootContainsExecutable = folders.Any(folder =>
            SafePaths.IsRootDestination(folder.Destination)
            && ResolveManifestPath(folder.Directory, manifestDirectory) is string root
            && File.Exists(Path.Combine(root, manifest.Application.Executable)));

        if (rootContainsExecutable)
        {
            return;
        }

        if (!folders.Any(folder => SafePaths.IsRootDestination(folder.Destination)))
        {
            errors.Add(
                "source.folders must contain a folder mapped to the installation "
                + "root (destination '.').");
        }
        else
        {
            errors.Add("application.executable is missing from the root source folder(s).");
        }
    }

    /// <summary>
    /// Applies the checks that are shared by every validation mode: the
    /// application identity fields and the installation scope and directory.
    /// </summary>
    private static void ValidateCommon(InstallerManifest manifest, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(manifest.Application.Id)
            || manifest.Application.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add("application.id is required and must be a valid key.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Application.Name)
            || manifest.Application.Name.IndexOfAny(
                Path.GetInvalidFileNameChars()) >= 0)
        {
            errors.Add(
                "application.name is required and must be a valid file name.");
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

        if (!SafePaths.IsSafeInstallTemplate(manifest.Installation.Directory))
        {
            errors.Add("installation.directory must resolve to an absolute, safe path.");
        }
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
    /// Indicates whether a destination targets the installation root itself:
    /// empty or <c>.</c>.
    /// </summary>
    public static bool IsRootDestination(string destination) =>
        string.IsNullOrWhiteSpace(destination) || destination == ".";

    /// <summary>
    /// Indicates whether a destination is an absolute location on the target
    /// machine (a rooted path or a template such as <c>{LocalAppData}</c>
    /// that resolves to a fully qualified path), as opposed to a location
    /// inside the installation root.
    /// </summary>
    public static bool IsExternalDestination(string destination)
    {
        if (IsRootDestination(destination))
        {
            return false;
        }

        return Path.IsPathRooted(destination)
            || Path.IsPathFullyQualified(ResolveInstallDirectory(destination, "app"));
    }

    /// <summary>
    /// Indicates whether an installation destination is acceptable: empty or
    /// <c>.</c> for the installation root, a safe relative subfolder, or an
    /// absolute path (possibly a template with placeholders) without parent
    /// or current directory segments.
    /// </summary>
    public static bool IsSafeDestination(string destination)
    {
        if (IsRootDestination(destination))
        {
            return true;
        }

        if (!Path.IsPathRooted(destination))
        {
            return IsSafeRelative(destination);
        }

        return Path.IsPathFullyQualified(destination)
            && !destination
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is ".." or ".");
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

    /// <summary>
    /// Indicates whether an install directory template is acceptable: it must
    /// not contain parent traversal segments and it must resolve to a fully
    /// qualified path once the known placeholders are substituted.
    /// </summary>
    /// <param name="template">Install directory template.</param>
    public static bool IsSafeInstallTemplate(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return false;
        }

        if (template.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is ".."))
        {
            return false;
        }

        return Path.IsPathFullyQualified(ResolveInstallDirectory(template, "app"));
    }

    /// <summary>
    /// Indicates whether a target directory can be used as the installation
    /// destination for an application. A missing or empty directory is always
    /// acceptable; a non-empty directory is accepted only when it already
    /// contains an installation manifest for the same application (an
    /// in-place upgrade). This prevents blind overwrites of unrelated folders.
    /// </summary>
    /// <param name="directory">Target installation directory.</param>
    /// <param name="applicationId">Stable identifier of the application.</param>
    public static bool IsCompatibleInstallDirectory(string directory, string applicationId)
    {
        if (!Directory.Exists(directory))
        {
            return true;
        }

        if (!Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return true;
        }

        var marker = Path.Combine(directory, ".od.installer-installed.json");

        if (!File.Exists(marker))
        {
            return false;
        }

        try
        {
            var installed = JsonFiles.ReadInstalledManifest(marker);
            return string.Equals(
                installed.ApplicationId,
                applicationId,
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
