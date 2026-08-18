using System.IO.Compression;

namespace OD.Installer.Core;

/// <summary>
/// Describes a file copy progress update.
/// </summary>
public readonly record struct FileCopyProgress(int Current, int Total, string RelativePath);

/// <summary>
/// Describes the outcome of a safe directory copy.
/// </summary>
public sealed record CopyResult(
    IReadOnlyList<string> Files,
    IReadOnlyList<string> CreatedDirectories);

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
    /// Copies all files of a directory into a target directory, validating
    /// every relative path, and reporting progress and cancellation between
    /// files. The copy is performed synchronously; callers that must not block
    /// the UI should wrap it in a background task.
    /// </summary>
    /// <param name="source">Source directory.</param>
    /// <param name="destination">Destination root directory.</param>
    /// <param name="progress">Optional progress receiver, invoked once per file.</param>
    /// <param name="cancellationToken">Token checked between file copies.</param>
    /// <param name="beforeCopy">
    /// Optional callback invoked with the resolved target path immediately before
    /// each file is copied. It can be used to capture files for rollback.
    /// </param>
    /// <returns>The relative paths of the copied files and the directories that were created.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when a relative path attempts to escape the target directory.
    /// </exception>
    public static CopyResult CopySafely(
        string source,
        string destination,
        IProgress<FileCopyProgress>? progress = null,
        CancellationToken cancellationToken = default,
        Action<string>? beforeCopy = null)
    {
        var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var existingDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Directory.Exists(destination))
        {
            foreach (var directory in Directory.EnumerateDirectories(
                destination,
                "*",
                SearchOption.AllDirectories))
            {
                existingDirectories.Add(Path.GetFullPath(directory));
            }
        }

        var createdFiles = new List<string>(files.Count);
        var createdDirectories = new List<string>();

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var relative = Path.GetRelativePath(source, file);

            if (!SafePaths.TryResolveUnderRoot(destination, relative, out var target))
            {
                throw new InvalidDataException("Unsafe file path.");
            }

            var targetDirectory = Path.GetDirectoryName(target)!;

            if (!existingDirectories.Contains(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
                createdDirectories.Add(targetDirectory);
                existingDirectories.Add(targetDirectory);
            }

            beforeCopy?.Invoke(target);
            File.Copy(file, target, true);
            createdFiles.Add(relative);

            progress?.Report(new FileCopyProgress(i + 1, files.Count, relative));
        }

        return new CopyResult(createdFiles, createdDirectories);
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
