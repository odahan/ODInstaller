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
        if (ResolveAbsolute(manifest.Output.Directory) is null) errors.Add("output.directory must be an absolute path.");
        if (string.IsNullOrWhiteSpace(manifest.Output.FileName) || Path.GetFileName(manifest.Output.FileName) != manifest.Output.FileName) errors.Add("output.fileName must be a file name only.");
        var source = ResolveAbsolute(manifest.Source.Directory);
        if (source is null || !Directory.Exists(source)) errors.Add("source.directory must be an existing absolute directory.");
        else if (!File.Exists(Path.Combine(source, manifest.Application.Executable))) errors.Add("application.executable is missing from source.directory.");
        if (manifest.License.RequireAcceptance && (ResolveAbsolute(manifest.License.File) is not string license || !File.Exists(license))) errors.Add("license.file must be an existing absolute file when acceptance is required.");
        if (!string.IsNullOrWhiteSpace(manifest.Application.WelcomeImage))
        {
            var image = ResolveAbsolute(manifest.Application.WelcomeImage);
            if (image is null || !File.Exists(image) || !string.Equals(Path.GetExtension(image), ".png", StringComparison.OrdinalIgnoreCase)) errors.Add("application.welcomeImage must reference an existing PNG file by an absolute path.");
        }
        if (!string.IsNullOrWhiteSpace(manifest.Application.Icon))
        {
            var icon = ResolveAbsolute(manifest.Application.Icon);
            if (icon is null || !File.Exists(icon) || !string.Equals(Path.GetExtension(icon), ".ico", StringComparison.OrdinalIgnoreCase)) errors.Add("application.icon must reference an existing ICO file by an absolute path.");
        }
        return new ValidationResult(errors);
    }
    public static string? ResolveAbsolute(string path) => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : null;
    public static string ResolveOutput(string directory) => ResolveAbsolute(directory) ?? throw new InvalidDataException("output.directory must be an absolute path.");
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
