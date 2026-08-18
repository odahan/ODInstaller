namespace OD.Installer.Core;

/// <summary>
/// Prepares a new installation beside the active installation and swaps the
/// directories while retaining the previous version for rollback.
/// </summary>
public sealed class InstallationDirectoryTransaction
{
    private bool isSwapped;
    private bool isCompleted;

    /// <summary>
    /// Initializes a transaction and creates its empty staging directory.
    /// </summary>
    /// <param name="targetDirectory">Final application installation directory.</param>
    public InstallationDirectoryTransaction(string targetDirectory)
    {
        TargetDirectory = Path.GetFullPath(
            Path.TrimEndingDirectorySeparator(targetDirectory));

        var parent = Path.GetDirectoryName(TargetDirectory)
            ?? throw new InvalidOperationException(
                "The installation directory must have a parent directory.");
        var name = Path.GetFileName(TargetDirectory);
        var identifier = Guid.NewGuid().ToString("N");

        Directory.CreateDirectory(parent);

        StagingDirectory = Path.Combine(
            parent,
            $".{name}.od-staging-{identifier}");
        BackupDirectory = Path.Combine(
            parent,
            $".{name}.od-backup-{identifier}");

        Directory.CreateDirectory(StagingDirectory);
    }

    /// <summary>
    /// Gets the final installation directory.
    /// </summary>
    public string TargetDirectory { get; }

    /// <summary>
    /// Gets the directory in which the new version must be prepared.
    /// </summary>
    public string StagingDirectory { get; }

    /// <summary>
    /// Gets the temporary location of the previous version during the swap.
    /// </summary>
    public string BackupDirectory { get; }

    /// <summary>
    /// Replaces the target directory with the prepared staging directory.
    /// </summary>
    /// <remarks>
    /// If activating the staged directory fails after the previous version was
    /// moved, the previous directory is immediately restored before the error
    /// is propagated.
    /// </remarks>
    public void Swap()
    {
        if (isCompleted || isSwapped)
        {
            throw new InvalidOperationException(
                "The installation directory transaction has already been committed.");
        }

        var hadPreviousInstallation = Directory.Exists(TargetDirectory);

        if (hadPreviousInstallation)
        {
            Directory.Move(TargetDirectory, BackupDirectory);
        }

        try
        {
            Directory.Move(StagingDirectory, TargetDirectory);
            isSwapped = true;
        }
        catch
        {
            if (hadPreviousInstallation
                && Directory.Exists(BackupDirectory)
                && !Directory.Exists(TargetDirectory))
            {
                Directory.Move(BackupDirectory, TargetDirectory);
            }

            throw;
        }
    }

    /// <summary>
    /// Makes the new installation permanent and removes the previous version.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the backup was removed, or
    /// <see langword="false"/> when cleanup must be retried later.
    /// </returns>
    public bool Complete()
    {
        if (!isSwapped)
        {
            throw new InvalidOperationException(
                "The installation directory has not been swapped.");
        }

        isCompleted = true;

        try
        {
            if (Directory.Exists(BackupDirectory))
            {
                Directory.Delete(BackupDirectory, recursive: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Removes the staged or active new version and restores the previous one.
    /// </summary>
    public void Rollback()
    {
        if (isCompleted)
        {
            return;
        }

        if (isSwapped && Directory.Exists(TargetDirectory))
        {
            Directory.Delete(TargetDirectory, recursive: true);
        }

        if (Directory.Exists(BackupDirectory))
        {
            Directory.Move(BackupDirectory, TargetDirectory);
        }

        if (Directory.Exists(StagingDirectory))
        {
            Directory.Delete(StagingDirectory, recursive: true);
        }

        isSwapped = false;
    }
}

/// <summary>
/// Saves individual files before they are overwritten and restores them when
/// an installation operation fails.
/// </summary>
public sealed class FileBackupTransaction
{
    private readonly Dictionary<string, BackupEntry> entries =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string backupDirectory = Path.Combine(
        Path.GetTempPath(),
        $"od.installer-backup-{Guid.NewGuid():N}");
    private bool isCompleted;

    /// <summary>
    /// Records the current state of a file before it is written or deleted.
    /// Repeated captures of the same path preserve the original state only.
    /// </summary>
    /// <param name="path">Full path of the file that may be changed.</param>
    public void Capture(string path)
    {
        if (isCompleted)
        {
            throw new InvalidOperationException(
                "The file backup transaction has already been completed.");
        }

        var fullPath = Path.GetFullPath(path);

        if (entries.ContainsKey(fullPath))
        {
            return;
        }

        if (!File.Exists(fullPath))
        {
            entries.Add(fullPath, new BackupEntry(fullPath, null));
            return;
        }

        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            entries.Count.ToString(
                "D8",
                System.Globalization.CultureInfo.InvariantCulture));

        File.Copy(fullPath, backupPath);
        entries.Add(fullPath, new BackupEntry(fullPath, backupPath));
    }

    /// <summary>
    /// Discards all captured originals after a successful installation.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when temporary backups were removed, or
    /// <see langword="false"/> when cleanup could not be completed.
    /// </returns>
    public bool Complete()
    {
        isCompleted = true;

        try
        {
            if (Directory.Exists(backupDirectory))
            {
                Directory.Delete(backupDirectory, recursive: true);
            }

            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Restores captured files and removes files that did not exist originally.
    /// </summary>
    public void Rollback()
    {
        if (isCompleted)
        {
            return;
        }

        var errors = new List<Exception>();

        foreach (var entry in entries.Values.Reverse())
        {
            try
            {
                if (entry.BackupPath is null)
                {
                    if (File.Exists(entry.Path))
                    {
                        File.Delete(entry.Path);
                    }

                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.Path)!);
                File.Copy(entry.BackupPath, entry.Path, overwrite: true);
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count == 0)
        {
            try
            {
                if (Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
            }
            catch (Exception exception)
            {
                errors.Add(exception);
            }
        }

        if (errors.Count > 0)
        {
            throw new AggregateException(
                "One or more files could not be restored.",
                errors);
        }
    }

    private sealed record BackupEntry(string Path, string? BackupPath);
}
