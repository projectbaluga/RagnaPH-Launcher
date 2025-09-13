using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Checksum;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace RagnaPH.Patching;

/// <summary>
/// Low level reader for THOR archives. The implementation aims to support all
/// variants produced by official tools and rAthena. It validates headers,
/// offsets and CRC checksums and exposes the contained entries for later
/// application to a GRF archive.
/// </summary>
public sealed class ThorArchive : IDisposable
{
    private const string Magic = "ASSF (C) 2007 Aeomin DEV";

    private readonly string _path;
    private readonly Header _header;
    private readonly List<ThorEntry> _entries;

    private ThorArchive(string path, Header header, List<ThorEntry> entries)
    {
        _path = path;
        _header = header;
        _entries = entries;
    }

    /// <summary>Gets the target GRF file name suggested by the archive.</summary>
    public string? TargetGrf => _header.TargetGrf;

    /// <summary>Gets the list of file entries contained in the archive.</summary>
    public IReadOnlyList<ThorEntry> Entries => _entries;

    /// <summary>Opens a THOR archive from disk.</summary>
    /// <exception cref="InvalidDataException">Thrown when the archive is malformed.</exception>
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

        if (entry.CompressedSize > int.MaxValue)
            throw new InvalidDataException("THOR: BAD_TABLE OOB size");

        var buffer = new byte[checked((int)entry.CompressedSize)];
        using (var fs = File.OpenRead(_path))
        {
            fs.Position = entry.Offset;
            var read = await fs.ReadAsync(buffer, 0, buffer.Length);
            if (read != buffer.Length)
                throw new InvalidDataException("THOR: BAD_COMPRESSION");
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

        if ((uint)data.Length != entry.UncompressedSize)
            throw new InvalidDataException("THOR: BAD_COMPRESSION");

        var crc32 = new Crc32();
        crc32.Update(data);
        if ((uint)crc32.Value != entry.Crc)
            throw new InvalidDataException($"THOR: BAD_CRC {entry.VirtualPath}");

        return new MemoryStream(data, writable: false);
    }

    private static Header ReadHeader(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(24));
        if (magic != Magic)
            throw new InvalidDataException("THOR: BAD_HEADER");

        byte version = reader.ReadByte();
        uint fileCount = reader.ReadUInt32();
        short mode = reader.ReadInt16();

        string targetGrf = string.Empty;
        uint tableSize = 0;
        uint tableOffset = 0;
        long headerEnd;
        bool tableCompressed;

        switch (mode)
        {
            case 0x30:
                int len = reader.ReadByte();
                targetGrf = Encoding.ASCII.GetString(reader.ReadBytes(len));
                tableSize = reader.ReadUInt32();
                tableOffset = reader.ReadUInt32();
                tableCompressed = true;
                headerEnd = reader.BaseStream.Position;
                break;
            case 0x21:
                int len2 = reader.ReadByte();
                targetGrf = Encoding.ASCII.GetString(reader.ReadBytes(len2));
                reader.ReadByte(); // padding
                tableCompressed = false;
                tableOffset = (uint)reader.BaseStream.Position;
                headerEnd = reader.BaseStream.Position;
                tableSize = (uint)(stream.Length - tableOffset);
                break;
            default:
                throw new InvalidDataException("THOR: BAD_HEADER");
        }

        long fileLength = stream.Length;
        long tableOffsetLong = tableOffset;
        long tableSizeLong = tableSize;

        bool Valid(long off) => off >= headerEnd && off + tableSizeLong == fileLength;

        if (!Valid(tableOffsetLong))
        {
            long rel = headerEnd + tableOffsetLong;
            if (Valid(rel))
            {
                tableOffsetLong = rel;
            }
            else
            {
                long expected = fileLength - tableSizeLong;
                if (Valid(expected))
                {
                    tableOffsetLong = expected;
                }
                else
                {
                    throw new InvalidDataException($"THOR: BAD_TABLE offset/size mismatch (offset {tableOffsetLong}, size {tableSizeLong}, length {fileLength})");
                }
            }
        }

        return new Header(version, (int)fileCount, targetGrf, tableSizeLong, tableOffsetLong, headerEnd, tableCompressed);
    }

    private static List<ThorEntry> ReadEntries(Stream fs, string thorPath, Header header)
    {
        var entries = new List<ThorEntry>();
        fs.Position = header.FileTableOffset;

        var tableBuf = new byte[header.FileTableSize];
        var read = fs.Read(tableBuf, 0, tableBuf.Length);
        if (read != tableBuf.Length)
            throw new InvalidDataException("THOR: BAD_TABLE truncated table");

        // Auto-detect zlib regardless of header flag
        byte[] tableData = tableBuf;
        if (tableBuf.Length >= 2 && tableBuf[0] == 0x78 &&
            (tableBuf[1] == 0x01 || tableBuf[1] == 0x9C || tableBuf[1] == 0xDA))
        {
            tableData = DecompressZlib(tableBuf);
        }

        int index = 0;
        int pos = 0;
        while (pos < tableData.Length)
        {
            // Try to read C-string path
            string path;
            int nul = Array.IndexOf<byte>(tableData, 0, pos);
            if (nul >= 0 && nul + 1 + 16 <= tableData.Length)
            {
                path = Encoding.UTF8.GetString(tableData, pos, nul - pos);
                pos = nul + 1;
            }
            else
            {
                // Fallback to UInt16 length-prefixed
                if (pos + 2 > tableData.Length)
                    throw new InvalidDataException($"THOR: BAD_TABLE truncated entry {index}");
                int nameLen = tableData[pos] | (tableData[pos + 1] << 8);
                pos += 2;
                if (nameLen < 0 || pos + nameLen > tableData.Length)
                    throw new InvalidDataException($"THOR: BAD_TABLE truncated entry {index}");
                path = Encoding.UTF8.GetString(tableData, pos, nameLen);
                pos += nameLen;
            }

            path = NormalizeEntryPath(path);

            // Read the four UInt32 fields
            if (pos + 16 > tableData.Length)
                throw new InvalidDataException($"THOR: BAD_TABLE truncated entry {index} {path}");
            uint compSize = BitConverter.ToUInt32(tableData, pos);
            pos += 4;
            uint uncompSize = BitConverter.ToUInt32(tableData, pos);
            pos += 4;
            uint dataOffset = BitConverter.ToUInt32(tableData, pos);
            pos += 4;
            uint crc = BitConverter.ToUInt32(tableData, pos);
            pos += 4;

            byte flags = 0;
            // Optional flags byte before alignment
            if (pos < tableData.Length && pos % 4 == 1)
            {
                flags = tableData[pos];
                pos++;
            }

            int aligned = (pos + 3) & ~3;
            if (aligned > tableData.Length)
                throw new InvalidDataException($"THOR: BAD_TABLE truncated entry {index} {path}");
            pos = aligned;

            long dataPos = checked(header.DataOffset + (long)dataOffset);
            long endPos = checked(dataPos + (long)compSize);
            if (dataPos < header.DataOffset || endPos > header.FileTableOffset)
                throw new InvalidDataException($"THOR: BAD_TABLE OOB entry {index} {path}");

            entries.Add(new ThorEntry(path, flags, (uint)dataPos,
                compSize, uncompSize, crc));

            index++;
        }

        if (index != header.FileCount)
            throw new InvalidDataException("THOR: BAD_TABLE count mismatch");

        return entries;
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        if (TryDecompress(data, true, out var result) ||
            TryDecompress(data, false, out result))
        {
            return result;
        }
        throw new InvalidDataException("THOR: BAD_COMPRESSION");
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
            throw new InvalidDataException("THOR: BAD_HEADER");

        var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
        {
            if (seg == "." || seg == "..")
                throw new InvalidDataException("THOR: BAD_HEADER");
        }

        return string.Join("/", segments);
    }

    public void Dispose()
    {
        // Nothing to dispose; streams are opened on demand.
    }

    private sealed record Header(byte Version, int FileCount, string TargetGrf,
        long FileTableSize, long FileTableOffset, long DataOffset, bool TableCompressed);

    /// <summary>Represents a file entry in the THOR archive.</summary>
    public sealed record ThorEntry(string VirtualPath, byte Flags, uint Offset,
        uint CompressedSize, uint UncompressedSize, uint Crc)
    {
        public ThorEntryKind Kind => (Flags & 0x01) == 0x01
            ? ThorEntryKind.Delete
            : ThorEntryKind.File;
    }

    /// <summary>Type of THOR entry.</summary>
    public enum ThorEntryKind { File, Delete, Directory }
}

