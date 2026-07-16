using System.Text.Json;
using System.Text.RegularExpressions;

namespace OdInstaller.Core;

/// <summary>
/// Describes the full configuration required to generate an installer.
/// </summary>
public sealed class InstallerManifest
{
    /// <summary>
    /// General information about the application.
    /// </summary>
    public ApplicationManifest Application { get; init; } = new();

    /// <summary>
    /// Location of the application source files.
    /// </summary>
    public SourceManifest Source { get; init; } = new();

    /// <summary>
    /// Settings related to the installation.
    /// </summary>
    public InstallationManifest Installation { get; init; } = new();

    /// <summary>
    /// Settings related to the license.
    /// </summary>
    public LicenseManifest License { get; init; } = new();

    /// <summary>
    /// Configuration of the shortcuts to create.
    /// </summary>
    public ShortcutManifest Shortcuts { get; init; } = new();

    /// <summary>
    /// Configuration of the generated installer file.
    /// </summary>
    public OutputManifest Output { get; init; } = new();
}

/// <summary>
/// Describes the application to install.
/// </summary>
public sealed class ApplicationManifest
{
    /// <summary>
    /// Stable identifier of the application.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>
    /// Display name of the application.
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Version of the application.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Name of the publisher or developer.
    /// </summary>
    public string Publisher { get; init; } = string.Empty;

    /// <summary>
    /// Relative path to the main executable.
    /// </summary>
    public string Executable { get; init; } = string.Empty;

    /// <summary>
    /// Optional path to the application icon.
    /// </summary>
    public string? Icon { get; init; }

    /// <summary>
    /// Optional path to the image displayed in the installation wizard.
    /// </summary>
    public string? WelcomeImage { get; init; }
}

/// <summary>
/// Describes the directory containing the application files.
/// </summary>
public sealed class SourceManifest
{
    /// <summary>
    /// Path of the source directory.
    /// </summary>
    public string Directory { get; init; } = string.Empty;
}

/// <summary>
/// Describes the installation options.
/// </summary>
public sealed class InstallationManifest
{
    /// <summary>
    /// Scope of the installation. Version 1 only supports <c>perUser</c>.
    /// </summary>
    public string Scope { get; init; } = "perUser";

    /// <summary>
    /// Target installation directory.
    /// </summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the user can choose the installation directory.
    /// </summary>
    public bool AllowDirectorySelection { get; init; } = true;
}

/// <summary>
/// Describes the license presented during installation.
/// </summary>
public sealed class LicenseManifest
{
    /// <summary>
    /// Path to the license file.
    /// </summary>
    public string File { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the user must accept the license.
    /// </summary>
    public bool RequireAcceptance { get; init; } = true;
}

/// <summary>
/// Describes the shortcuts to create.
/// </summary>
public sealed class ShortcutManifest
{
    /// <summary>
    /// Indicates whether a shortcut must be created in the Start menu.
    /// </summary>
    public bool StartMenu { get; init; } = true;

    /// <summary>
    /// Indicates whether a shortcut must be created on the desktop.
    /// </summary>
    public bool Desktop { get; init; }
}

/// <summary>
/// Describes the installer file to generate.
/// </summary>
public sealed class OutputManifest
{
    /// <summary>
    /// Output directory.
    /// </summary>
    public string Directory { get; init; } = string.Empty;

    /// <summary>
    /// Name of the installer file.
    /// </summary>
    public string FileName { get; init; } = string.Empty;
}

/// <summary>
/// Describes the elements recorded after installation.
/// </summary>
public sealed class InstalledManifest
{
    /// <summary>
    /// Identifier of the installed application.
    /// </summary>
    public string ApplicationId { get; init; } = string.Empty;

    /// <summary>
    /// Version of the installed application.
    /// </summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>
    /// Installation directory.
    /// </summary>
    public string InstallDirectory { get; init; } = string.Empty;

    /// <summary>
    /// List of installed files.
    /// </summary>
    public List<string> Files { get; init; } = [];

    /// <summary>
    /// List of created directories.
    /// </summary>
    public List<string> Directories { get; init; } = [];

    /// <summary>
    /// List of created shortcuts.
    /// </summary>
    public List<string> Shortcuts { get; init; } = [];

    /// <summary>
    /// List of created registry keys.
    /// </summary>
    public List<string> RegistryKeys { get; init; } = [];

    /// <summary>
    /// Installation date and time, in UTC.
    /// </summary>
    public DateTimeOffset InstalledAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Path to the uninstaller program.
    /// </summary>
    public string UninstallerPath { get; init; } = string.Empty;
}

/// <summary>
/// Provides operations for reading and writing configuration JSON files.
/// </summary>
public static class JsonFiles
{
    /// <summary>
    /// Common options used to serialize and deserialize JSON files.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Expression used to fix a legacy value format wrapped in double quotes.
    /// </summary>
    private static readonly Regex LegacyDoubleQuotedValue = new(
        "(?<prefix>:\\s*)\"\"(?<value>[^\"\\r\\n]*)\"\"(?=\\s*[,}])",
        RegexOptions.Compiled);

    /// <summary>
    /// Reads and deserializes an installer manifest.
    /// </summary>
    /// <param name="path">Path of the JSON file.</param>
    /// <returns>The deserialized manifest.</returns>
    public static InstallerManifest ReadManifest(string path)
    {
        var content = File.ReadAllText(path);

        try
        {
            return DeserializeManifest(content);
        }
        catch (JsonException)
        {
            // Compatibility with legacy files containing double quotes.
            var normalized = LegacyDoubleQuotedValue.Replace(
                content,
                match =>
                    match.Groups["prefix"].Value
                    + JsonSerializer.Serialize(match.Groups["value"].Value));

            // If no fix was applied, keep the original exception.
            if (string.Equals(content, normalized, StringComparison.Ordinal))
            {
                throw;
            }

            return DeserializeManifest(normalized);
        }
    }

    /// <summary>
    /// Deserializes the content of a manifest.
    /// </summary>
    private static InstallerManifest DeserializeManifest(string content)
    {
        return JsonSerializer.Deserialize<InstallerManifest>(content, Options)
            ?? throw new InvalidDataException("The manifest is empty.");
    }

    /// <summary>
    /// Writes the installed manifest to disk.
    /// </summary>
    /// <param name="path">Path of the target file.</param>
    /// <param name="manifest">Manifest to write.</param>
    public static void WriteInstalledManifest(string path, InstalledManifest manifest)
    {
        var content = JsonSerializer.Serialize(manifest, Options);
        File.WriteAllText(path, content);
    }

    /// <summary>
    /// Reads an already recorded installed manifest.
    /// </summary>
    /// <param name="path">Path of the JSON file.</param>
    /// <returns>The deserialized manifest.</returns>
    public static InstalledManifest ReadInstalledManifest(string path)
    {
        return JsonSerializer.Deserialize<InstalledManifest>(
                   File.ReadAllText(path),
                   Options)
               ?? throw new InvalidDataException(
                   "The installed manifest is invalid.");
    }
}
