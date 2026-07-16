using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OdInstaller.Core;

/// <summary>
/// Handles the creation and reading of the self-contained installer package
/// (a ZIP payload appended at the end of the installer host executable).
/// </summary>
public static class PackageFormat
{
    /// <summary>
    /// Marker written right after the payload to identify and locate it
    /// at the end of the installer executable.
    /// </summary>
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("ODINST01");

    /// <summary>
    /// Builds the installer executable by copying the host executable and
    /// appending a ZIP payload containing the manifest, license, uninstaller
    /// and application files.
    /// </summary>
    /// <param name="host">Path to the installer host executable template.</param>
    /// <param name="output">Path of the installer executable to generate.</param>
    /// <param name="source">Directory containing the application files.</param>
    /// <param name="normalizedManifest">Path to the normalized manifest file.</param>
    /// <param name="license">Path to the license file.</param>
    /// <param name="uninstaller">Path to the uninstaller executable.</param>
    /// <param name="welcomeImage">Optional path to the welcome wizard image.</param>
    /// <param name="icon">Optional path to the application icon.</param>
    public static void Append(
        string host,
        string output,
        string source,
        string normalizedManifest,
        string license,
        string uninstaller,
        string? welcomeImage,
        string? icon)
    {
        // Copy the host executable as the base of the generated installer.
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Copy(host, output, true);

        // Build the ZIP payload in memory before appending it to the executable.
        using var payload = new MemoryStream();
        using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true))
        {
            Add(zip, normalizedManifest, "installer.json");
            Add(zip, license, "LICENSE.txt");
            Add(zip, uninstaller, "uninstaller/OdInstaller.Uninstaller.exe");

            if (!string.IsNullOrWhiteSpace(welcomeImage))
            {
                Add(zip, welcomeImage, "welcome.png");
            }

            if (!string.IsNullOrWhiteSpace(icon))
            {
                Add(zip, icon, "application.ico");
            }

            foreach (var file in FileInventory.Enumerate(source))
            {
                Add(zip, Path.Combine(source, file), "app/" + file.Replace('\\', '/'));
            }
        }

        // Append the payload bytes, the marker, and the payload length (as a footer)
        // so the payload can be located and extracted from the end of the file.
        var bytes = payload.ToArray();
        using var stream = new FileStream(output, FileMode.Append, FileAccess.Write);
        stream.Write(bytes);
        stream.Write(Marker);

        Span<byte> length = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(length, bytes.Length);
        stream.Write(length);
    }

    /// <summary>
    /// Opens the ZIP payload embedded at the end of an installer executable.
    /// </summary>
    /// <param name="executable">Path to the installer executable.</param>
    /// <returns>A stream positioned at the start of the payload.</returns>
    /// <exception cref="InvalidDataException">
    /// Thrown when the payload marker is missing or the payload length is invalid.
    /// </exception>
    public static Stream OpenPayload(string executable)
    {
        var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (stream.Length < 16)
        {
            throw new InvalidDataException("Installer payload is missing.");
        }

        // Read the 16-byte footer (8-byte marker + 8-byte payload length).
        stream.Seek(-16, SeekOrigin.End);
        var footer = new byte[16];
        stream.ReadExactly(footer);

        if (!footer.AsSpan(0, 8).SequenceEqual(Marker))
        {
            throw new InvalidDataException("Installer payload marker is invalid.");
        }

        var length = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(8));

        if (length <= 0 || length > stream.Length - 16)
        {
            throw new InvalidDataException("Installer payload length is invalid.");
        }

        // Copy the payload into memory and dispose of the source stream.
        stream.Seek(stream.Length - 16 - length, SeekOrigin.Begin);
        var payload = new MemoryStream();
        stream.CopyTo(payload, (int)Math.Min(length, 81920));
        payload.Position = 0;
        stream.Dispose();

        return payload;
    }

    /// <summary>
    /// Adds a single file to the ZIP archive under the given entry name.
    /// </summary>
    private static void Add(ZipArchive zip, string source, string name)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var input = File.OpenRead(source);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
