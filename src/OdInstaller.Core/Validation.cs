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
        if (string.IsNullOrWhiteSpace(manifest.Output.FileName) || Path.GetFileName(manifest.Output.FileName) != manifest.Output.FileName) errors.Add("output.fileName must be a file name only.");
        var source = Resolve(manifestDirectory, manifest.Source.Directory);
        if (source is null || !Directory.Exists(source)) errors.Add("source.directory does not exist or escapes the manifest directory.");
        else if (!File.Exists(Path.Combine(source, manifest.Application.Executable))) errors.Add("application.executable is missing from source.directory.");
        if (manifest.License.RequireAcceptance && (Resolve(manifestDirectory, manifest.License.File) is not string license || !File.Exists(license))) errors.Add("license.file is required and must exist when acceptance is required.");
        return new ValidationResult(errors);
    }
    public static string? Resolve(string root, string relative) => SafePaths.TryResolveUnderRoot(root, relative, out var path) ? path : null;
}

public static class SafePaths
{
    public static bool IsSafeRelative(string path) => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(x => x is ".." or ".") && Path.GetFileName(path).IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    public static bool TryResolveUnderRoot(string root, string relative, out string fullPath)
    {
        fullPath = ""; if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative)) return false;
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!candidate.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) return false;
        fullPath = candidate; return true;
    }
    public static string ResolveInstallDirectory(string template, string applicationName) => template.Replace("{LocalAppData}", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase).Replace("<Application>", applicationName, StringComparison.OrdinalIgnoreCase);
}
