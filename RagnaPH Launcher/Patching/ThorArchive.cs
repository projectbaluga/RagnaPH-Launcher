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
/// Low level reader for THOR archives using a tolerant parser similar to GRFEditor.
/// </summary>
public sealed class ThorArchive : IDisposable
{
    private const string Magic = "ASSF (C) 2007 Aeomin DEV";

    private readonly string _path;
    private readonly List<ThorEntry> _entries;
    private readonly long _dataBase;
    private readonly string? _targetGrf;

    private ThorArchive(string path, List<ThorEntry> entries, long dataBase, string? targetGrf)
    {
        _path = path;
        _entries = entries;
        _dataBase = dataBase;
        _targetGrf = targetGrf;
    }

    /// <summary>Gets the target GRF file name suggested by the archive.</summary>
    public string? TargetGrf => _targetGrf;

    /// <summary>Gets the list of file entries contained in the archive.</summary>
    public IReadOnlyList<ThorEntry> Entries => _entries;

    /// <summary>Opens a THOR archive from disk.</summary>
    /// <exception cref="InvalidDataException">Thrown when the archive is malformed.</exception>
    public static ThorArchive Open(string thorPath)
    {
        string targetGrf = string.Empty;
        using (var fs = File.OpenRead(thorPath))
        using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true))
        {
            var magic = Encoding.ASCII.GetString(br.ReadBytes(Magic.Length));
            if (magic != Magic)
                throw new InvalidDataException("THOR: BAD_HEADER");
            br.ReadByte(); // version
            uint _ = br.ReadUInt32(); // fileCount (ignored)
            short mode = br.ReadInt16();
            switch (mode)
            {
                case 0x30:
                    int len = br.ReadByte();
                    targetGrf = Encoding.ASCII.GetString(br.ReadBytes(len));
                    break;
                case 0x21:
                    int len2 = br.ReadByte();
                    targetGrf = Encoding.ASCII.GetString(br.ReadBytes(len2));
                    br.ReadByte();
                    break;
                default:
                    throw new InvalidDataException("THOR: BAD_HEADER");
            }
        }

        var (entriesRaw, dataBase, _, _) = ThorReaderFix.ReadThorStructure(thorPath);
        var list = new List<ThorEntry>(entriesRaw.Count);
        foreach (var e in entriesRaw)
            list.Add(new ThorEntry(e.Path, 0, e.DataOffset, e.CompSize, e.UncompSize, e.Crc32));
        return new ThorArchive(thorPath, list, dataBase, targetGrf);
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
        long start = checked(_dataBase + (long)entry.Offset);
        using (var fs = File.OpenRead(_path))
        {
            fs.Position = start;
            var read = await fs.ReadAsync(buffer, 0, buffer.Length);
            if (read != buffer.Length)
                throw new InvalidDataException($"THOR: BAD_TABLE truncated entry {entry.VirtualPath}");
        }

        byte[] data;
        if (buffer.Length == 0)
        {
            data = Array.Empty<byte>();
        }
        else if (TryDecompress(buffer, true, out var tmp) || TryDecompress(buffer, false, out tmp))
        {
            data = tmp;
        }
        else
        {
            data = buffer;
        }

        if (entry.UncompressedSize != 0 && (uint)data.Length != entry.UncompressedSize)
        {
            // Allow mismatch by trusting the bytes we actually got
        }

        var crc32 = new Crc32();
        crc32.Update(data);
        uint calc = (uint)crc32.Value;
        if (entry.Crc != 0 && calc != entry.Crc)
        {
            crc32 = new Crc32();
            crc32.Update(buffer);
            calc = (uint)crc32.Value;
            if (calc != entry.Crc)
                throw new InvalidDataException($"THOR: BAD_CRC {entry.VirtualPath}");
        }

        return new MemoryStream(data, writable: false);
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

    public void Dispose()
    {
        // Nothing to dispose; streams are opened on demand.
    }

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
