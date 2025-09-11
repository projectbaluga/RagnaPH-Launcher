using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    /// <summary>
    /// Parser for Thor "ASSF" archives produced by GRF Editor. Each archive
    /// may contain an index zlib block describing payload offsets or a single
    /// streamed zlib block with consecutive file records. This class validates
    /// bounds, decodes paths and exposes the file entries for merging into a
    /// GRF.
    /// </summary>
    internal sealed class ThorArchive
    {
        internal sealed class ThorEntry
        {
            public ThorEntry(string path, byte[] data)
            {
                Path = path;
                Data = data;
            }

            public string Path { get; }
            public byte[] Data { get; }
        }

        private sealed class IndexEntry
        {
            public IndexEntry(string path, uint offset, uint compressed, uint decompressed)
            {
                Path = path;
                Offset = offset;
                Compressed = compressed;
                Decompressed = decompressed;
            }

            public string Path { get; }
            public uint Offset { get; }
            public uint Compressed { get; }
            public uint Decompressed { get; }
        }

        public string TargetGrf { get; }
        public string PatchMode { get; }
        public IReadOnlyList<ThorEntry> Entries { get; }

        private ThorArchive(string targetGrf, string patchMode, List<ThorEntry> entries)
        {
            TargetGrf = targetGrf;
            PatchMode = patchMode;
            Entries = entries;
        }

        /// <summary>
        /// Opens and parses a Thor archive.
        /// </summary>
        /// <param name="path">File system path to the .thor archive.</param>
        /// <param name="progress">Optional progress reporter for phase information.</param>
        public static ThorArchive Open(string path, IProgress<string> progress = null)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            byte[] fileBytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(fileBytes))
            using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
            {
                progress?.Report("Header");

                // Validate magic
                var magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'S' ||
                    magic[2] != 'S' || magic[3] != 'F')
                    throw new InvalidDataException("Invalid .thor file header.");

                // Read metadata fields as null-terminated strings
                string patchMode = ReadCString(br);
                string targetGrf = ReadCString(br);

                if (string.IsNullOrWhiteSpace(targetGrf))
                    targetGrf = "data.grf";

                int metaEnd = (int)ms.Position;

                // Determine initial index pointer from header if present
                int indexOffset = -1;
                if (ms.Position + 4 <= ms.Length)
                {
                    long posBefore = ms.Position;
                    try
                    {
                        int candidate = br.ReadInt32();
                        if (IsValidZlibHeader(fileBytes, candidate))
                            indexOffset = candidate;
                        else
                            ms.Position = posBefore;
                    }
                    catch
                    {
                        ms.Position = posBefore;
                    }
                }

                progress?.Report("Index");

                if (indexOffset < 0)
                {
                    indexOffset = FindPreviousZlibHeader(fileBytes, metaEnd, fileBytes.Length - 2);
                }

                List<IndexEntry> indexEntries = null;
                while (indexOffset >= metaEnd)
                {
                    try
                    {
                        byte[] indexData = DecompressPayload(fileBytes, indexOffset,
                            fileBytes.Length - indexOffset);
                        if (TryParseIndex(indexData, fileBytes.Length, out indexEntries))
                            break;
                    }
                    catch
                    {
                        // ignore and step to previous header
                    }

                    indexOffset = FindPreviousZlibHeader(fileBytes, metaEnd, indexOffset - 1);
                }

                List<ThorEntry> entries;

                if (indexEntries != null && indexEntries.Count > 0)
                {
                    progress?.Report($"Extract {indexEntries.Count} files");

                    entries = new List<ThorEntry>(indexEntries.Count);
                    foreach (var e in indexEntries)
                    {
                        byte[] data = DecompressPayload(fileBytes, (int)e.Offset, (int)e.Compressed);
                        if (data.Length != e.Decompressed)
                            throw new InvalidDataException("decompressed length mismatch");
                        entries.Add(new ThorEntry(e.Path, data));
                    }
                }
                else
                {
                    progress?.Report("Streamed");
                    entries = ParseStreamed(fileBytes, metaEnd);
                }

                return new ThorArchive(targetGrf, patchMode, entries);
            }
        }

        private static bool TryParseIndex(byte[] indexData, long fileLength, out List<IndexEntry> entries)
        {
            entries = new List<IndexEntry>();

            using (var ms = new MemoryStream(indexData))
            using (var br = new BinaryReader(ms))
            {
                bool counted = false;
                uint expected = 0;
                if (indexData.Length >= 4)
                {
                    expected = BitConverter.ToUInt32(indexData, 0);
                    long minLen = (long)expected * 17 + 4;
                    if (minLen <= indexData.Length)
                    {
                        counted = true;
                        br.ReadUInt32();
                    }
                }

                if (counted)
                {
                    for (uint i = 0; i < expected; i++)
                    {
                        if (!TryReadRecord(br, fileLength, stopOnTag: false, out var rec))
                            break;
                        if (rec != null)
                            entries.Add(rec);
                    }
                }
                else
                {
                    while (TryReadRecord(br, fileLength, stopOnTag: true, out var rec))
                    {
                        if (rec != null)
                            entries.Add(rec);
                    }
                }
            }

            return entries.Count > 0;
        }

        private static bool TryReadRecord(BinaryReader br, long fileLength, bool stopOnTag, out IndexEntry entry)
        {
            entry = null;
            var ms = br.BaseStream;

            if (ms.Length - ms.Position < 1)
                return false;

            byte tag = br.ReadByte();
            if (stopOnTag && (tag == 0 || tag == 0xFF))
                return false;

            // Read path bytes
            var pathBytes = ReadCStringBytes(br);
            if (pathBytes == null)
                return false; // truncated path

            if (ms.Length - ms.Position < 16)
                return false; // truncated record

            uint offset = br.ReadUInt32();
            uint comp = br.ReadUInt32();
            uint decomp = br.ReadUInt32();
            br.ReadUInt32(); // crc

            if (comp == 0 || decomp == 0 || (long)offset + comp > fileLength)
                throw new InvalidDataException("invalid payload region");

            string path;
            try
            {
                path = DecodePath(pathBytes);
            }
            catch
            {
                return true; // skip undecodable path
            }

            path = path.Replace('\\', '/');
            if (path.StartsWith("/"))
                path = "data" + path;
            path = NormalizePath(path);

            if (!string.IsNullOrEmpty(path))
                entry = new IndexEntry(path, offset, comp, decomp);

            return true;
        }

        private static byte[] ReadCStringBytes(BinaryReader br)
        {
            var ms = br.BaseStream;
            long start = ms.Position;
            while (ms.Position < ms.Length)
            {
                if (br.ReadByte() == 0)
                {
                    long end = ms.Position - 1;
                    int len = (int)(end - start);
                    ms.Position = start;
                    var bytes = br.ReadBytes(len);
                    br.ReadByte(); // consume terminator
                    return bytes;
                }
            }
            ms.Position = ms.Length; // move to end on failure
            return null;
        }

        private static bool IsValidZlibHeader(byte[] buffer, int offset)
        {
            if (offset < 0 || offset + 2 > buffer.Length)
                return false;
            byte cmf = buffer[offset];
            byte flg = buffer[offset + 1];
            if (cmf != 0x78)
                return false;
            if (flg != 0x01 && flg != 0x5E && flg != 0x9C && flg != 0xDA)
                return false;
            int combined = (cmf << 8) | flg;
            return (cmf & 0x0F) == 8 && combined % 31 == 0;
        }

        private static int FindPreviousZlibHeader(byte[] data, int start, int from)
        {
            for (int i = Math.Min(from, data.Length - 2); i >= start; i--)
            {
                if (IsValidZlibHeader(data, i))
                    return i;
            }
            return -1;
        }

        private static int FindNextZlibHeader(byte[] data, int start)
        {
            for (int i = Math.Max(start, 0); i <= data.Length - 2; i++)
            {
                if (IsValidZlibHeader(data, i))
                    return i;
            }
            return -1;
        }

        private static List<ThorEntry> ParseStreamed(byte[] fileBytes, int metaEnd)
        {
            int zlibOffset = FindNextZlibHeader(fileBytes, metaEnd);
            if (zlibOffset < 0)
                throw new InvalidDataException("stream block not found");

            byte[] streamData = DecompressPayload(fileBytes, zlibOffset, fileBytes.Length - zlibOffset);
            var entries = new List<ThorEntry>();

            using (var ms = new MemoryStream(streamData))
            using (var br = new BinaryReader(ms))
            {
                while (ms.Position < ms.Length)
                {
                    if (!TryReadStreamRecord(br, out var entry))
                        break;
                    if (entry != null)
                        entries.Add(entry);
                }
            }

            return entries;
        }

        private static bool TryReadStreamRecord(BinaryReader br, out ThorEntry entry)
        {
            entry = null;
            var ms = br.BaseStream;

            if (ms.Length - ms.Position < 1)
                return false;

            long start = ms.Position;

            // Try variant A (length-prefixed)
            if (ms.Length - ms.Position >= 8)
            {
                int pathLen = br.ReadInt32();
                int dataLen = br.ReadInt32();

                if (pathLen >= 0 && dataLen >= 0)
                {
                    long remaining = ms.Length - ms.Position;

                    if (remaining >= pathLen + dataLen + 4)
                    {
                        int peekFlags = br.ReadInt32();
                        if (peekFlags != 0 && peekFlags != 1)
                            ms.Position -= 4;
                    }

                    if (ms.Length - ms.Position >= pathLen + dataLen)
                    {
                        var pathBytes = br.ReadBytes(pathLen);
                        if (ms.Length - ms.Position < dataLen)
                            return false;
                        var dataBytes = br.ReadBytes(dataLen);

                        string path;
                        try
                        {
                            path = DecodePath(pathBytes);
                        }
                        catch
                        {
                            return true; // skip undecodable path
                        }

                        path = path.Replace('\\', '/');
                        path = path.TrimStart('/');
                        path = NormalizePath(path);
                        if (!string.IsNullOrEmpty(path))
                            entry = new ThorEntry(path, dataBytes);

                        return true;
                    }
                }
            }

            // Reset and try variant B (CString path)
            ms.Position = start;
            var cPathBytes = ReadCStringBytes(br);
            if (cPathBytes == null)
                return false;
            if (ms.Length - ms.Position < 4)
                return false;
            int len = br.ReadInt32();
            if (len < 0 || ms.Length - ms.Position < len)
                return false;
            var cData = br.ReadBytes(len);

            string cPath;
            try
            {
                cPath = DecodePath(cPathBytes);
            }
            catch
            {
                return true; // skip undecodable path
            }

            cPath = cPath.Replace('\\', '/');
            cPath = cPath.TrimStart('/');
            cPath = NormalizePath(cPath);
            if (!string.IsNullOrEmpty(cPath))
                entry = new ThorEntry(cPath, cData);

            return true;
        }

        private static byte[] DecompressPayload(byte[] buffer, int offset, int count)
        {
            // Try with zlib header first
            try
            {
                using (var ms = new MemoryStream(buffer, offset, count))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch
            {
            }

            if (count <= 2)
                throw new InvalidDataException("decompression failed");

            try
            {
                using (var ms = new MemoryStream(buffer, offset + 2, count - 2))
                using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("decompression failed", ex);
            }
        }

        private static string ReadCString(BinaryReader br)
        {
            var bytes = new List<byte>();
            byte b;
            while (br.BaseStream.Position < br.BaseStream.Length &&
                   (b = br.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return bytes.Count > 0 ? Encoding.ASCII.GetString(bytes.ToArray()) : string.Empty;
        }

        private static string DecodePath(byte[] pathBytes)
        {
            Encoding[] encodings = new Encoding[]
            {
                Encoding.GetEncoding(
                    949,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback), // CP949
                Encoding.GetEncoding(
                    1252,
                    EncoderFallback.ExceptionFallback,
                    DecoderFallback.ExceptionFallback), // Windows-1252
                new UTF8Encoding(false, true),
            };

            foreach (var enc in encodings)
            {
                try
                {
                    return enc.GetString(pathBytes);
                }
                catch (DecoderFallbackException)
                {
                    // try next encoding
                }
            }

            throw new InvalidDataException("entry path undecodable");
        }

        private static string NormalizePath(string path)
        {
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var stack = new Stack<string>();
            foreach (var part in parts)
            {
                if (part == ".") continue;
                if (part == "..")
                {
                    if (stack.Count > 0) stack.Pop();
                    continue;
                }
                stack.Push(part);
            }
            var arr = stack.ToArray();
            Array.Reverse(arr);
            var joined = string.Join("/", arr);
            return joined.IndexOfAny(Path.GetInvalidPathChars()) >= 0 ? null : joined;
        }
    }
}

