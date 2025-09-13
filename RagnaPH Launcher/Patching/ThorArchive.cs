using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib;

namespace RagnaPH.Patching;

/// <summary>
/// Low level reader for THOR archives. The implementation aims to support all
/// variants produced by official tools and rAthena. It validates headers,
/// offsets and CRC checksums and exposes the contained entries for later
/// application to a GRF archive.
/// </summary>
public sealed class ThorArchive : IDisposable
{
    private readonly string _path;
    private readonly Header _header;
    private readonly List<ThorEntry> _entries;

    private ThorArchive(string path, Header header, List<ThorEntry> entries)
    {
        _path = path;
        _header = header;
        _entries = entries;
    }

    /// <summary>
    /// Gets the target GRF file name suggested by the archive. May be
    /// <c>null</c> if the archive does not specify one.
    /// </summary>
    public string? TargetGrf => _header.TargetGrf;

    /// <summary>
    /// Gets the list of file entries contained in the archive.
    /// </summary>
    public IReadOnlyList<ThorEntry> Entries => _entries;

    /// <summary>
    /// Opens a THOR archive from disk and parses its header and file table.
    /// </summary>
    /// <param name="thorPath">Path to the THOR file.</param>
    /// <exception cref="InvalidDataException">Thrown when the archive is
    /// malformed.</exception>
    public static ThorArchive Open(string thorPath)
    {
        using var fs = File.OpenRead(thorPath);
        var header = ReadHeader(fs);
        var entries = ReadEntries(fs, thorPath, header);
        return new ThorArchive(thorPath, header, entries);
    }

    /// <summary>
    /// Opens the payload stream for the specified entry. The returned stream is
    /// decompressed and validated against the entry's CRC32 value.
    /// </summary>
    public async Task<Stream> OpenEntryStreamAsync(ThorEntry entry)
    {
        if (entry.Kind != ThorEntryKind.File)
            return Stream.Null;

        var buffer = new byte[entry.CompressedSize];
        using (var fs = File.OpenRead(_path))
        {
            fs.Position = entry.Offset;
            var read = await fs.ReadAsync(buffer, 0, buffer.Length);
            if (read != buffer.Length)
                throw new InvalidDataException("Payload corruption");
        }

        byte[] data;
        if (entry.CompressedSize == 0)
        {
            data = Array.Empty<byte>();
        }
        else if (entry.CompressedSize == entry.UncompressedSize)
        {
            data = buffer;
        }
        else
        {
            data = DecompressZlib(buffer);
        }

        if (data.Length != entry.UncompressedSize)
            throw new InvalidDataException("Payload corruption");

        var crc32 = new Crc32();
        crc32.Update(data);
        if ((uint)crc32.Value != entry.Crc)
            throw new InvalidDataException("Payload corruption");

        return new MemoryStream(data, writable: false);
    }

    private static Header ReadHeader(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(24));
        if (magic != "ASSF (C) 2007 Aeomin DEV")
            throw new InvalidDataException("Bad THOR header");

        byte version = reader.ReadByte();
        int fileCount = reader.ReadInt32();
        short mode = reader.ReadInt16();

        string targetGrf = string.Empty;
        int tableCompressedLength = 0;
        int tableOffset = 0;
        int dataOffset = 0;

        switch (mode)
        {
            case 0x30:
                int len = reader.ReadByte();
                targetGrf = Encoding.ASCII.GetString(reader.ReadBytes(len));
                tableCompressedLength = reader.ReadInt32();
                tableOffset = reader.ReadInt32();
                dataOffset = (int)reader.BaseStream.Position;
                break;
            case 0x21:
                int len2 = reader.ReadByte();
                targetGrf = Encoding.ASCII.GetString(reader.ReadBytes(len2));
                reader.ReadByte(); // padding
                tableOffset = (int)reader.BaseStream.Position;
                dataOffset = tableOffset;
                break;
            default:
                throw new InvalidDataException("Bad THOR header");
        }

        if (tableOffset <= dataOffset)
            throw new InvalidDataException("File table offset mismatch");

        return new Header(version, fileCount, targetGrf, tableCompressedLength,
            tableOffset, dataOffset, mode);
    }

    private static List<ThorEntry> ReadEntries(Stream fs, string thorPath, Header header)
    {
        var entries = new List<ThorEntry>();

        fs.Position = header.FileTableOffset;
        byte[] tableData;
        if (header.FileTableCompressedLength > 0)
        {
            var compressed = new byte[header.FileTableCompressedLength];
            fs.Read(compressed, 0, compressed.Length);
            tableData = DecompressZlib(compressed);
        }
        else
        {
            tableData = new byte[fs.Length - fs.Position];
            fs.Read(tableData, 0, tableData.Length);
        }

        using var tableStream = new MemoryStream(tableData);
        using var reader = new BinaryReader(tableStream, Encoding.ASCII);
        while (tableStream.Position < tableStream.Length)
        {
            byte nameLen = reader.ReadByte();
            var rawName = Encoding.ASCII.GetString(reader.ReadBytes(nameLen));
            var name = NormalizeEntryPath(rawName);
            byte flags = reader.ReadByte();
            uint offset = reader.ReadUInt32();
            int sizeCompressed = reader.ReadInt32();
            int sizeDecompressed = reader.ReadInt32();
            uint crc = reader.ReadUInt32();

            long dataPos = header.DataOffset + offset;
            if (dataPos + sizeCompressed > header.FileTableOffset)
                throw new InvalidDataException("File table offset mismatch");

            entries.Add(new ThorEntry(name, flags, (int)dataPos,
                sizeCompressed, sizeDecompressed, crc));
        }

        if (entries.Count != header.FileCount)
            throw new InvalidDataException("File table offset mismatch");

        return entries;
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        if (TryDecompress(data, true, out var result) ||
            TryDecompress(data, false, out result))
        {
            return result;
        }
        throw new InvalidDataException("Payload corruption");
    }

    private static bool TryDecompress(byte[] data, bool header, out byte[] result)
    {
        try
        {
            using var ms = new MemoryStream(data);
            using var inflater = new InflaterInputStream(ms, new Inflater(!header));
            using var outMs = new MemoryStream();
            inflater.CopyTo(outMs);
            result = outMs.ToArray();
            return true;
        }
        catch (SharpZipBaseException)
        {
            result = Array.Empty<byte>();
            return false;
        }
    }

    private static string NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("Bad THOR header");

        var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (seg == "." || seg == "..")
                throw new InvalidDataException("Bad THOR header");
        }

        return string.Join("/", segments);
    }

    public void Dispose()
    {
        // Nothing to dispose; streams are opened on demand.
    }

    private sealed record Header(byte Version, int FileCount, string TargetGrf,
        int FileTableCompressedLength, int FileTableOffset, int DataOffset, short Mode);

    /// <summary>
    /// Represents a file entry in the THOR archive.
    /// </summary>
    public sealed record ThorEntry(string VirtualPath, byte Flags, int Offset,
        int CompressedSize, int UncompressedSize, uint Crc)
    {
        public ThorEntryKind Kind => (Flags & 0x01) == 0x01
            ? ThorEntryKind.Delete
            : ThorEntryKind.File;
    }

    /// <summary>
    /// Type of THOR entry.
    /// </summary>
    public enum ThorEntryKind { File, Delete, Directory }
}

