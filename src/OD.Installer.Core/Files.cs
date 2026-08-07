using System.IO.Compression;

namespace OD.Installer.Core;

/// <summary>
/// Provides operations for enumerating and extracting files.
/// </summary>
public static class FileInventory
{
    /// <summary>
    /// Returns all files in a directory as relative paths.
    /// </summary>
    /// <param name="source">Source directory.</param>
    /// <returns>List of relative paths of the files found.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a junction point or a name collision is detected.
    /// </exception>
    public static IReadOnlyList<string> Enumerate(string source)
    {
        var result = new List<string>();

        // Walk the tree manually so reparse points are rejected before any
        // of their contents is enumerated. Directory.EnumerateFiles with
        // SearchOption.AllDirectories would silently traverse junctions.
        void Walk(string directory)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);

                // Junction points and symbolic links are rejected
                // to avoid escaping the source directory.
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException(
                        $"Reparse point not allowed: {entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Walk(entry);
                }
                else
                {
                    result.Add(Path.GetRelativePath(source, entry));
                }
            }
        }

        Walk(source);

        // A collision can occur on case-insensitive file systems.
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != result.Count)
        {
            throw new InvalidDataException("File name collision detected.");
        }

        return result;
    }

    /// <summary>
    /// Extracts a ZIP archive into a directory, validating each entry path.
    /// </summary>
    /// <param name="zipStream">Stream containing the ZIP archive.</param>
    /// <param name="destination">Destination directory.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when an archive entry attempts to escape the target directory.
    /// </exception>
    public static void ExtractSafely(Stream zipStream, string destination)
    {
        using var archive = new ZipArchive(
            zipStream,
            ZipArchiveMode.Read,
            leaveOpen: false);

        foreach (var entry in archive.Entries)
        {
            // Entries representing a directory only are skipped.
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            if (!SafePaths.TryResolveUnderRoot(
                    destination,
                    entry.FullName,
                    out var target))
            {
                throw new InvalidDataException(
                    $"Unsafe archive entry: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }
}
