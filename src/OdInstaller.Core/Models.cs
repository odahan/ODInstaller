using System.Text.Json;

namespace OdInstaller.Core;

public sealed class InstallerManifest
{
    public ApplicationManifest Application { get; init; } = new();
    public SourceManifest Source { get; init; } = new();
    public InstallationManifest Installation { get; init; } = new();
    public LicenseManifest License { get; init; } = new();
    public ShortcutManifest Shortcuts { get; init; } = new();
    public OutputManifest Output { get; init; } = new();
}
public sealed class ApplicationManifest { public string Id { get; init; } = ""; public string Name { get; init; } = ""; public string Version { get; init; } = ""; public string Publisher { get; init; } = ""; public string Executable { get; init; } = ""; public string? Icon { get; init; } public string? WelcomeImage { get; init; } }
public sealed class SourceManifest { public string Directory { get; init; } = ""; }
public sealed class InstallationManifest { public string Scope { get; init; } = "perUser"; public string Directory { get; init; } = ""; public bool AllowDirectorySelection { get; init; } = true; }
public sealed class LicenseManifest { public string File { get; init; } = ""; public bool RequireAcceptance { get; init; } = true; }
public sealed class ShortcutManifest { public bool StartMenu { get; init; } = true; public bool Desktop { get; init; } }
public sealed class OutputManifest { public string Directory { get; init; } = ""; public string FileName { get; init; } = ""; }

public sealed class InstalledManifest
{
    public string ApplicationId { get; init; } = ""; public string Version { get; init; } = ""; public string InstallDirectory { get; init; } = "";
    public List<string> Files { get; init; } = []; public List<string> Directories { get; init; } = []; public List<string> Shortcuts { get; init; } = []; public List<string> RegistryKeys { get; init; } = [];
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow; public string UninstallerPath { get; init; } = "";
}

public static class JsonFiles
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public static InstallerManifest ReadManifest(string path) => JsonSerializer.Deserialize<InstallerManifest>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The manifest is empty.");
    public static void WriteInstalledManifest(string path, InstalledManifest manifest) => File.WriteAllText(path, JsonSerializer.Serialize(manifest, Options));
    public static InstalledManifest ReadInstalledManifest(string path) => JsonSerializer.Deserialize<InstalledManifest>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("The installed manifest is invalid.");
}
