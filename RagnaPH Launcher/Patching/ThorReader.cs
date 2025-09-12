using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Reads THOR archives produced by the official patcher. The format begins
/// with a fixed header followed by the file data and a compressed file table.
/// Only the features required by the launcher are implemented here.
/// </summary>
public sealed class ThorReader : IThorReader
{
    public Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var fs = File.OpenRead(thorPath);
            var header = ReadHeader(fs);
            return Task.FromResult(new ThorManifest(header.TargetGrf, IncludesChecksums: false));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            throw new InvalidDataException($"Invalid or corrupt THOR archive '{Path.GetFileName(thorPath)}'.", ex);
        }
    }

    public Task<IEnumerable<ThorEntry>> ReadEntriesAsync(string thorPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var fs = File.OpenRead(thorPath);
            var header = ReadHeader(fs);
            return Task.FromResult(ParseEntries(thorPath, header));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or EndOfStreamException)
        {
            throw new InvalidDataException($"Invalid or corrupt THOR archive '{Path.GetFileName(thorPath)}'.", ex);
        }
    }

    public void Dispose()
    {
        // nothing to dispose
    }

    private static Header ReadHeader(Stream stream)
    {
        var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(24));
        if (magic != "ASSF (C) 2007 Aeomin DEV")
            throw new InvalidDataException("Not a THOR archive");
        bool useGrfMerging = reader.ReadByte() != 0;
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
                break;
            default:
                throw new InvalidDataException($"Unknown THOR mode {mode}.");
        }
        return new Header(mode, targetGrf, tableCompressedLength, tableOffset, dataOffset, fileCount, useGrfMerging);
    }

    private static IEnumerable<ThorEntry> ParseEntries(string thorPath, Header header)
    {
        return header.Mode switch
        {
            0x30 => ParseMode30(thorPath, header),
            0x21 => ParseMode21(thorPath, header),
            _ => throw new InvalidDataException($"Unknown THOR mode {header.Mode}.")
        };
    }

    private static IEnumerable<ThorEntry> ParseMode21(string thorPath, Header header)
    {
        using var fs = File.OpenRead(thorPath);
        fs.Position = header.FileTableOffset;
        using var reader = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true);
        int sizeCompressed = reader.ReadInt32();
        int sizeDecompressed = reader.ReadInt32();
        byte nameLen = reader.ReadByte();
        var rawName = Encoding.ASCII.GetString(reader.ReadBytes(nameLen));
        var name = NormalizeEntryPath(rawName);
        long dataPos = fs.Position;
        return new[]
        {
            new ThorEntry(name, ThorEntryKind.File, sizeDecompressed, sizeCompressed, null,
                () => ReadFileAsync(thorPath, dataPos, sizeCompressed, sizeDecompressed))
        };
    }

    private static IEnumerable<ThorEntry> ParseMode30(string thorPath, Header header)
    {
        using var fs = File.OpenRead(thorPath);
        fs.Position = header.FileTableOffset;
        byte[] compressedTable = new byte[header.FileTableCompressedLength];
        fs.Read(compressedTable, 0, compressedTable.Length);
        var tableData = DecompressZlib(compressedTable);
        using var tableStream = new MemoryStream(tableData);
        using var reader = new BinaryReader(tableStream, Encoding.ASCII);
        var list = new List<ThorEntry>();
        while (tableStream.Position < tableStream.Length)
        {
            byte nameLen = reader.ReadByte();
            var rawName = Encoding.ASCII.GetString(reader.ReadBytes(nameLen));
            var name = NormalizeEntryPath(rawName);
            byte flags = reader.ReadByte();
            if ((flags & 0x01) == 0x01)
            {
                list.Add(new ThorEntry(name, ThorEntryKind.Delete, 0, 0, null,
                    () => Task.FromResult<Stream>(Stream.Null)));
                continue;
            }
            uint offset = reader.ReadUInt32();
            int sizeCompressed = reader.ReadInt32();
            int sizeDecompressed = reader.ReadInt32();
            long dataPos = header.DataOffset + offset;
            list.Add(new ThorEntry(name, ThorEntryKind.File, sizeDecompressed, sizeCompressed, null,
                () => ReadFileAsync(thorPath, dataPos, sizeCompressed, sizeDecompressed)));
        }
        return list;
    }

    private static async Task<Stream> ReadFileAsync(string thorPath, long offset, int sizeCompressed, int sizeDecompressed)
    {
        var buffer = new byte[sizeCompressed];
        using (var fs = File.OpenRead(thorPath))
        {
            fs.Position = offset;
            int read = await fs.ReadAsync(buffer, 0, sizeCompressed);
            if (read != sizeCompressed)
                throw new InvalidDataException("Unexpected end of THOR file");
        }
        if (sizeCompressed == sizeDecompressed)
        {
            // Some THOR entries may store data without compression. If the
            // compressed and decompressed sizes are equal we can return the
            // raw buffer directly instead of attempting to decompress it.
            return new MemoryStream(buffer, writable: false);
        }

        var data = DecompressZlib(buffer);
        if (data.Length != sizeDecompressed)
            throw new InvalidDataException("Decompression size mismatch");
        return new MemoryStream(data, writable: false);
    }

    private static byte[] DecompressZlib(byte[] data)
    {
        if (TryDecompress(data, skipHeader: false, out var result) ||
            TryDecompress(data, skipHeader: true, out result))
        {
            return result;
        }
        throw new InvalidDataException("Invalid zlib data");
    }

    private static bool TryDecompress(byte[] data, bool skipHeader, out byte[] result)
    {
        try
        {
            using var ms = new MemoryStream(data);
            if (skipHeader)
            {
                if (ms.ReadByte() == -1 || ms.ReadByte() == -1)
                {
                    result = Array.Empty<byte>();
                    return true;
                }
            }
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            ds.CopyTo(outMs);
            result = outMs.ToArray();
            return true;
        }
        catch (InvalidDataException)
        {
            result = Array.Empty<byte>();
            return false;
        }
    }

    private static string NormalizeEntryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidDataException("THOR entry name is empty");

        var segments = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            throw new InvalidDataException("THOR entry name is empty");

        foreach (var seg in segments)
        {
            if (seg == "." || seg == "..")
                throw new InvalidDataException("Invalid THOR entry path");
        }

        return string.Join("/", segments);
    }

    private sealed record Header(short Mode, string TargetGrf, int FileTableCompressedLength,
        int FileTableOffset, int DataOffset, int FileCount, bool UseGrfMerging);
}

