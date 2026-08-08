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
    /// Current format version of the payload footer.
    /// </summary>
    public const int FormatVersion = 2;

    /// <summary>
    /// Marker written right after the payload to identify and locate it
    /// at the end of the installer executable.
    /// </summary>
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("ODINST02");

    /// <summary>
    /// Marker used by the version 1 footer (no format version field).
    /// Still recognized for compatibility with earlier generated installers.
    /// </summary>
    private static readonly byte[] LegacyMarker = Encoding.ASCII.GetBytes("ODINST01");

    /// <summary>
    /// How many bytes after the footer are tolerated when locating it. This
    /// leaves room for a code-signature certificate table appended at the end
    /// of the file after the package was built.
    /// </summary>
    private const int MaxFooterTrailingBytes = 128 * 1024;

    /// <summary>
    /// "PK\x05\x06", the ZIP end-of-central-directory signature expected
    /// immediately before the footer.
    /// </summary>
    private static readonly byte[] EndOfCentralDirectorySignature = [0x50, 0x4B, 0x05, 0x06];

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

                // After the ZIP is finalized, append the footer: the marker,
                // the format version and the payload length. The footer is
                // written right after the ZIP's end-of-central-directory.
                var payloadLength = stream.Position;
                stream.Write(Marker);

                Span<byte> version = stackalloc byte[4];
                BinaryPrimitives.WriteInt32LittleEndian(version, FormatVersion);
                stream.Write(version);

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

        try
        {
            if (!TryLocateFooter(stream, out var payloadStart, out var payloadLength))
            {
                throw new InvalidDataException("Installer payload is missing or invalid.");
            }

            // Return a bounded window over the payload so the ZIP is read
            // without copying the whole archive into memory. The returned
            // stream owns the underlying file and disposes it.
            stream.Seek(payloadStart, SeekOrigin.Begin);
            return new PayloadStream(stream, payloadStart, payloadLength);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Locates the payload footer by scanning the tail of the file backwards.
    /// The footer is not required to be at the absolute end of the file: a
    /// bounded amount of trailing data (for example a code-signature
    /// certificate table) is tolerated.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> and the payload bounds when a valid footer is
    /// found; otherwise <see langword="false"/>.
    /// </returns>
    private static bool TryLocateFooter(
        FileStream stream,
        out long payloadStart,
        out long payloadLength)
    {
        payloadStart = 0;
        payloadLength = 0;

        if (stream.Length < 16)
        {
            return false;
        }

        var tailLength = (int)Math.Min(stream.Length, MaxFooterTrailingBytes + 24);
        var tail = new byte[tailLength];
        stream.Seek(-tailLength, SeekOrigin.End);
        stream.ReadExactly(tail);
        var tailStart = stream.Length - tailLength;

        return TryLocateFooter(stream, tail, tailStart, Marker, hasVersion: true, out payloadStart, out payloadLength)
            || TryLocateFooter(stream, tail, tailStart, LegacyMarker, hasVersion: false, out payloadStart, out payloadLength);
    }

    /// <summary>
    /// Scans the tail buffer backwards for a specific footer marker and
    /// validates the footer content and the ZIP record before it.
    /// </summary>
    private static bool TryLocateFooter(
        FileStream stream,
        byte[] tail,
        long tailStart,
        byte[] marker,
        bool hasVersion,
        out long payloadStart,
        out long payloadLength)
    {
        payloadStart = 0;
        payloadLength = 0;

        var footerSize = hasVersion ? 20 : 16;

        for (var i = tail.Length - marker.Length; i >= 0; i--)
        {
            if (!tail.AsSpan(i, marker.Length).SequenceEqual(marker))
            {
                continue;
            }

            var footerStart = tailStart + i;

            if (footerStart + footerSize > stream.Length)
            {
                continue;
            }

            long length;

            if (hasVersion)
            {
                var version = BinaryPrimitives.ReadInt32LittleEndian(tail.AsSpan(i + 8, 4));

                if (version != FormatVersion)
                {
                    continue;
                }

                length = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 12, 8));
            }
            else
            {
                length = BinaryPrimitives.ReadInt64LittleEndian(tail.AsSpan(i + 8, 8));
            }

            if (length <= 0 || length > footerStart)
            {
                continue;
            }

            // The ZIP end-of-central-directory record ends exactly at the
            // footer, so its "PK\x05\x06" signature must be the four bytes
            // 22 bytes before the footer (our builder always writes a ZIP
            // without a comment). This rejects false marker matches inside
            // trailing data.
            const int endOfCentralDirectorySize = 22;

            if (footerStart - endOfCentralDirectorySize < 0)
            {
                continue;
            }

            stream.Seek(footerStart - endOfCentralDirectorySize, SeekOrigin.Begin);
            var eocd = new byte[4];
            stream.ReadExactly(eocd);

            if (!eocd.AsSpan().SequenceEqual(EndOfCentralDirectorySignature))
            {
                continue;
            }

            payloadStart = footerStart - length;
            payloadLength = length;
            return true;
        }

        return false;
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
