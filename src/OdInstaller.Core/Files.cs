using System.IO.Compression;

namespace OdInstaller.Core;
public static class FileInventory
{
    public static IReadOnlyList<string> Enumerate(string source)
    {
        var result = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException($"Reparse point not allowed: {file}");
            result.Add(Path.GetRelativePath(source, file));
        }
        if (result.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) throw new InvalidDataException("File name collision detected.");
        return result;
    }
    public static void ExtractSafely(Stream zipStream, string destination)
    {
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: false);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            if (!SafePaths.TryResolveUnderRoot(destination, entry.FullName, out var target)) throw new InvalidDataException($"Unsafe archive entry: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); entry.ExtractToFile(target, overwrite: true);
        }
    }
}
