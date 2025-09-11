using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    /// <summary>
    /// Parser for Thor "ASSF" archives produced by GRF Editor. Each archive
    /// contains several zlib-compressed blobs and an index describing the
    /// location of every payload. The index itself is the last zlib blob in the
    /// file. This class validates bounds, decodes paths and exposes the file
    /// entries for merging into a GRF.
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
                progress?.Report("Reading header");

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

                // Locate the index blob: prefer header pointer, otherwise scan from tail
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

                if (indexOffset < 0)
                {
                    indexOffset = FindLastZlibHeader(fileBytes, metaEnd);
                    if (indexOffset < 0)
                        throw new InvalidDataException("Index not found");
                }

                progress?.Report("Reading index");

                byte[] indexData = DecompressPayload(fileBytes, indexOffset,
                    fileBytes.Length - indexOffset);

                var entries = ParseIndex(indexData, fileBytes);

                return new ThorArchive(targetGrf, patchMode, entries);
            }
        }

        private static List<ThorEntry> ParseIndex(byte[] indexData, byte[] fileBytes)
        {
            var entries = new List<ThorEntry>();

            int pos = 0;
            while (pos < indexData.Length)
            {
                // tag byte (unused)
                _ = indexData[pos++];

                // path string (zero-terminated)
                int start = pos;
                while (pos < indexData.Length && indexData[pos] != 0) pos++;
                if (pos >= indexData.Length)
                    throw new InvalidDataException("index truncated");

                var pathBytes = new byte[pos - start];
                Buffer.BlockCopy(indexData, start, pathBytes, 0, pathBytes.Length);
                pos++; // skip terminator

                if (indexData.Length - pos < 16)
                    throw new InvalidDataException("index truncated");

                uint offset = BitConverter.ToUInt32(indexData, pos); pos += 4;
                uint comp = BitConverter.ToUInt32(indexData, pos); pos += 4;
                uint decomp = BitConverter.ToUInt32(indexData, pos); pos += 4;
                _ = BitConverter.ToUInt32(indexData, pos); pos += 4; // crc

                if (comp == 0 || decomp == 0 || offset + comp > fileBytes.Length)
                    throw new InvalidDataException("invalid payload region");

                string path = DecodePath(pathBytes);
                path = path.Replace('\\', '/');
                if (path.StartsWith("/"))
                    path = path.TrimStart('/');
                if (!path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                    path = "data/" + path;
                path = NormalizePath(path);

                byte[] data = DecompressPayload(fileBytes, (int)offset, (int)comp);
                if (data.Length != decomp)
                    throw new InvalidDataException("decompressed length mismatch");

                if (!string.IsNullOrEmpty(path))
                    entries.Add(new ThorEntry(path, data));
            }

            return entries;
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

        private static int FindLastZlibHeader(byte[] data, int start)
        {
            for (int i = data.Length - 2; i >= start; i--)
            {
                if (IsValidZlibHeader(data, i))
                    return i;
            }
            return -1;
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

