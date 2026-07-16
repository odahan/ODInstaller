namespace OdInstaller.Core;

public sealed record ValidationResult(IReadOnlyList<string> Errors) { public bool IsValid => Errors.Count == 0; }
public static class ManifestValidator
{
    public static ValidationResult Validate(InstallerManifest manifest, string manifestDirectory)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(manifest.Application.Id) || manifest.Application.Id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) errors.Add("application.id is required and must be a valid key.");
        if (string.IsNullOrWhiteSpace(manifest.Application.Name)) errors.Add("application.name is required.");
        if (!Version.TryParse(manifest.Application.Version, out _)) errors.Add("application.version must be a valid version.");
        if (string.IsNullOrWhiteSpace(manifest.Application.Executable) || !SafePaths.IsSafeRelative(manifest.Application.Executable)) errors.Add("application.executable must be a safe relative path.");
        if (!string.Equals(manifest.Installation.Scope, "perUser", StringComparison.OrdinalIgnoreCase)) errors.Add("installation.scope must be 'perUser' in V1.");
        if (ResolveManifestPath(manifest.Output.Directory, manifestDirectory) is null) errors.Add("output.directory must be a valid path.");
        if (string.IsNullOrWhiteSpace(manifest.Output.FileName) || Path.GetFileName(manifest.Output.FileName) != manifest.Output.FileName) errors.Add("output.fileName must be a file name only.");
        var source = ResolveManifestPath(manifest.Source.Directory, manifestDirectory);
        if (source is null || !Directory.Exists(source)) errors.Add("source.directory must be an existing directory.");
        else if (!File.Exists(Path.Combine(source, manifest.Application.Executable))) errors.Add("application.executable is missing from source.directory.");
        if (manifest.License.RequireAcceptance && (ResolveManifestPath(manifest.License.File, manifestDirectory) is not string license || !File.Exists(license))) errors.Add("license.file must be an existing file when acceptance is required.");
        if (!string.IsNullOrWhiteSpace(manifest.Application.WelcomeImage))
        {
            var image = ResolveManifestPath(manifest.Application.WelcomeImage, manifestDirectory);
            if (image is null || !File.Exists(image) || !string.Equals(Path.GetExtension(image), ".png", StringComparison.OrdinalIgnoreCase)) errors.Add("application.welcomeImage must reference an existing PNG file.");
        }
        if (!string.IsNullOrWhiteSpace(manifest.Application.Icon))
        {
            var icon = ResolveManifestPath(manifest.Application.Icon, manifestDirectory);
            if (icon is null || !File.Exists(icon) || !string.Equals(Path.GetExtension(icon), ".ico", StringComparison.OrdinalIgnoreCase)) errors.Add("application.icon must reference an existing ICO file.");
        }
        return new ValidationResult(errors);
    }
    public static string? ResolveAbsolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
    public static string? ResolveManifestPath(string path, string manifestDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(manifestDirectory)) return null;
        try { return Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(manifestDirectory, path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }
    public static string ResolveOutput(string directory) => ResolveAbsolute(directory) ?? throw new InvalidDataException("output.directory must be an absolute path.");
    public static string ResolveOutput(string directory, string manifestDirectory) => ResolveManifestPath(directory, manifestDirectory) ?? throw new InvalidDataException("output.directory must be a valid path.");
}

public static class SafePaths
{
    public static bool IsSafeRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x is ".." or ".") && Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    public static bool TryResolveUnderRoot(string root, string relative, out string fullPath)
    {
        fullPath = ""; if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return false;
        var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var rootFull = rootPath + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!string.Equals(candidate, rootPath, StringComparison.OrdinalIgnoreCase) && !candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = candidate; return true;
    }
    public static string ResolveInstallDirectory(string template, string applicationName) => template.Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase).Replace("<Application>", applicationName, StringComparison.OrdinalIgnoreCase);
}
