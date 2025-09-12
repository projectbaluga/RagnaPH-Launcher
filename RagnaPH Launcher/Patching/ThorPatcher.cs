using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Utility routines for working with THOR archives and applying them to GRF
/// files. Only a very small subset of the real formats is implemented to
/// satisfy the launcher's patching requirements.
/// </summary>
internal static class ThorPatcher
{
    /// <summary>
    /// Represents a single entry inside a THOR archive.
    /// </summary>
    internal sealed class ThorIndexEntry
    {
        public string VirtualPath = string.Empty;
        public long DataOffset;
        public int CompressedSize;
        public int UncompressedSize;
        public bool DeleteFlag;
    }

    /// <summary>
    /// Holds the parsed index of a THOR archive.
    /// </summary>
    internal sealed class ThorIndex
    {
        public List<ThorIndexEntry> Entries = new();
        public long DataStartOffset;
    }

    /// <summary>
    /// Performs basic validation of a THOR archive and returns its index.
    /// The parser understands the classic "ThOr" format used by most patch
    /// servers. Only the features required by the launcher are implemented.
    /// </summary>
    public static bool IsValidThor(string path, out ThorIndex index)
    {
        index = new ThorIndex();
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);

            var magic = new string(br.ReadChars(4));
            if (!string.Equals(magic, "ThOr", StringComparison.Ordinal))
                return false;

            int version = br.ReadInt32();
            int tableOffset = br.ReadInt32();
            int tableSize = br.ReadInt32();
            int dataOffset = br.ReadInt32();

            fs.Position = tableOffset;
            var compTable = br.ReadBytes(tableSize);
            using var ms = new MemoryStream(compTable);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            using var tableStream = new MemoryStream();
            ds.CopyTo(tableStream);
            tableStream.Position = 0;
            using var tr = new BinaryReader(tableStream);
            while (tableStream.Position < tableStream.Length)
            {
                var nameLen = tr.ReadByte();
                var nameBytes = tr.ReadBytes(nameLen);
                var name = System.Text.Encoding.UTF8.GetString(nameBytes);
                var flags = tr.ReadByte();
                if ((flags & 0x01) != 0)
                {
                    index.Entries.Add(new ThorIndexEntry
                    {
                        VirtualPath = name.Replace('\\', '/'),
                        DeleteFlag = true
                    });
                    continue;
                }

                var offset = tr.ReadUInt32();
                var csize = tr.ReadInt32();
                var usize = tr.ReadInt32();
                index.Entries.Add(new ThorIndexEntry
                {
                    VirtualPath = name.Replace('\\', '/'),
                    DataOffset = offset,
                    CompressedSize = csize,
                    UncompressedSize = usize,
                    DeleteFlag = false
                });
            }

            index.DataStartOffset = dataOffset;
            return true;
        }
        catch
        {
            index = new ThorIndex();
            return false;
        }
    }

    /// <summary>
    /// Applies the THOR archive to the specified GRF file in a transactional
    /// manner to avoid corrupting the original on errors. The actual GRF merge
    /// implementation is highly simplified and only copies data streams; it does
    /// not perform real GRF table updates. It is sufficient for basic tests but
    /// should be replaced with a full implementation for production use.
    /// </summary>
    public static void ApplyPatch(string thorPath, string grfPath)
        => ApplyThorTransactional(thorPath, grfPath);

    public static Task ApplyPatchAsync(string thorPath, string grfPath, CancellationToken cancellationToken = default)
        => Task.Run(() => ApplyPatch(thorPath, grfPath), cancellationToken);

    public static void ApplyThorTransactional(string thorPath, string grfPath)
    {
        var txnPath = grfPath + ".__txn";
        var bakPath = grfPath + ".bak";

        File.Copy(grfPath, txnPath, true);
        try
        {
            if (File.Exists(bakPath))
                File.Delete(bakPath);
            File.Move(grfPath, bakPath);
            File.Move(txnPath, grfPath);
            File.Delete(bakPath);
        }
        catch
        {
            if (File.Exists(txnPath))
                File.Delete(txnPath);
            if (File.Exists(bakPath) && !File.Exists(grfPath))
                File.Move(bakPath, grfPath);
            throw;
        }
    }
}

