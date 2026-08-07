using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OD.Installer.Core;

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
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        // Build the archive in a temporary file first so a failure cannot
        // leave a corrupt installer behind, then move it into place.
        var temporary = output + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            File.Copy(host, temporary);

            using (var stream = new FileStream(
                       temporary,
                       FileMode.Open,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                // Position at the end of the copied host, then stream the ZIP
                // payload directly to disk (no in-memory copy of the archive).
                stream.Seek(0, SeekOrigin.End);

                using (var zip = new ZipArchive(
                           stream,
                           ZipArchiveMode.Create,
                           leaveOpen: true))
                {
                    Add(zip, normalizedManifest, "installer.json");
                    Add(zip, license, "LICENSE.txt");
                    Add(zip, uninstaller, "uninstaller/OD.Installer.Uninstaller.exe");

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

                // After the ZIP is finalized, append the footer:
                // the marker followed by the payload length.
                var payloadLength = stream.Position;
                stream.Write(Marker);

                Span<byte> length = stackalloc byte[8];
                BinaryPrimitives.WriteInt64LittleEndian(length, payloadLength);
                stream.Write(length);
            }

            File.Move(temporary, output, true);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
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
        var stream = new FileStream(
            executable,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        if (stream.Length < 16)
        {
            stream.Dispose();
            throw new InvalidDataException("Installer payload is missing.");
        }

        // Read the 16-byte footer (8-byte marker + 8-byte payload length).
        stream.Seek(-16, SeekOrigin.End);
        var footer = new byte[16];
        stream.ReadExactly(footer);

        if (!footer.AsSpan(0, 8).SequenceEqual(Marker))
        {
            stream.Dispose();
            throw new InvalidDataException("Installer payload marker is invalid.");
        }

        var length = BinaryPrimitives.ReadInt64LittleEndian(footer.AsSpan(8));

        if (length <= 0 || length > stream.Length - 16)
        {
            stream.Dispose();
            throw new InvalidDataException("Installer payload length is invalid.");
        }

        // Return a bounded window over the payload so the ZIP is read without
        // copying the whole archive into memory. The returned stream owns the
        // underlying file and disposes it.
        var start = stream.Length - 16 - length;
        stream.Seek(start, SeekOrigin.Begin);
        return new PayloadStream(stream, start, length);
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

    /// <summary>
    /// A read-only window over a portion of an underlying stream, used to
    /// expose exactly the embedded ZIP payload without loading it into memory.
    /// </summary>
    private sealed class PayloadStream : Stream
    {
        private readonly Stream _base;
        private readonly long _start;
        private readonly long _length;
        private long _position;

        public PayloadStream(Stream baseStream, long start, long length)
        {
            _base = baseStream;
            _start = start;
            _length = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var remaining = _length - _position;

            if (remaining <= 0)
            {
                return 0;
            }

            count = (int)Math.Min(count, remaining);
            _base.Seek(_start + _position, SeekOrigin.Begin);
            var read = _base.Read(buffer, offset, count);
            _position += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin))
            };

            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _base.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
