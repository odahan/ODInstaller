using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OdInstaller.Core;
public static class PackageFormat
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("ODINST01");
    public static void Append(string host, string output, string source, string normalizedManifest, string license, string uninstaller)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(output)!); File.Copy(host, output, true);
        using var payload = new MemoryStream(); using (var zip = new ZipArchive(payload, ZipArchiveMode.Create, true))
        {
            Add(zip, normalizedManifest, "installer.json"); Add(zip, license, "LICENSE.txt"); Add(zip, uninstaller, "uninstaller/OdInstaller.Uninstaller.exe");
            foreach (var file in FileInventory.Enumerate(source)) Add(zip, Path.Combine(source, file), "app/" + file.Replace('\\', '/'));
        }
        var bytes = payload.ToArray(); using var stream = new FileStream(output, FileMode.Append, FileAccess.Write); stream.Write(bytes); stream.Write(Marker); Span<byte> length = stackalloc byte[8]; BinaryPrimitives.WriteInt64LittleEndian(length, bytes.Length); stream.Write(length);
    }
    public static Stream OpenPayload(string executable)
    {
        var stream = new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read); if (stream.Length < 16) throw new InvalidDataException("Installer payload is missing.");
        stream.Seek(-16, SeekOrigin.End); var footer = new byte[16]; stream.ReadExactly(footer); if (!footer.AsSpan(0, 8).SequenceEqual(Marker)) throw new InvalidDataException("Installer payload marker is invalid.");
        var length = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(8)); if (length <= 0 || length > stream.Length - 16) throw new InvalidDataException("Installer payload length is invalid.");
        stream.Seek(stream.Length - 16 - length, SeekOrigin.Begin); var payload = new MemoryStream(); stream.CopyTo(payload, (int)Math.Min(length, 81920)); payload.Position = 0; stream.Dispose(); return payload;
    }
    private static void Add(ZipArchive zip, string source, string name) { var entry = zip.CreateEntry(name, CompressionLevel.Optimal); using var input = File.OpenRead(source); using var output = entry.Open(); input.CopyTo(output); }
}
