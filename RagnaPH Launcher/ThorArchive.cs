using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    /// <summary>
    /// Resilient parser for traditional Thor "ASSF" archives.  The reader validates
    /// header fields, performs strict bounds checking and exposes the decoded file
    /// entries for merging into a GRF file.
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
                if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'S' || magic[2] != 'S' || magic[3] != 'F')
                    throw new InvalidDataException("Invalid .thor file header.");

                // Read metadata fields as null-terminated strings (patch mode and target GRF)
                string patchMode = ReadCString(br);
                string targetGrf = ReadCString(br);

                // Default target GRF if metadata is missing
                if (string.IsNullOrWhiteSpace(targetGrf))
                    targetGrf = "data.grf";

                int metaEnd = (int)ms.Position;

                // Try reading payload bounds from header
                int payloadOffset = 0;
                int payloadSize = 0;
                if (ms.Position + 8 <= ms.Length)
                {
                    long posBefore = ms.Position;
                    try
                    {
                        payloadOffset = br.ReadInt32();
                        payloadSize = br.ReadInt32();
                    }
                    catch (EndOfStreamException)
                    {
                        ms.Position = posBefore;
                        payloadOffset = 0;
                        payloadSize = 0;
                    }

                    if (!(payloadOffset >= metaEnd && payloadSize > 0 &&
                          payloadOffset + payloadSize <= fileBytes.Length))
                    {
                        // Discard invalid header values
                        payloadOffset = 0;
                        payloadSize = 0;
                    }
                }

                // If bounds not supplied or invalid, locate zlib header manually
                if (payloadOffset == 0 && payloadSize == 0)
                {
                    payloadOffset = FindZlibHeader(fileBytes, metaEnd);
                    if (payloadOffset < 0)
                        throw new InvalidDataException("zlib header not found");

                    payloadSize = fileBytes.Length - payloadOffset;
                }

                // Validate final bounds
                if (payloadOffset < metaEnd || payloadSize <= 0 ||
                    payloadOffset + payloadSize > fileBytes.Length)
                    throw new InvalidDataException("invalid bounds");

                progress?.Report("Decompressing");

                byte[] decompressed = DecompressPayload(fileBytes, payloadOffset, payloadSize);

                var entries = ParseEntries(decompressed);
                return new ThorArchive(targetGrf, patchMode, entries);
            }
        }

        private static List<ThorEntry> ParseEntries(byte[] data)
        {
            const int MinimalRecordSize = 8; // pathLen + dataLen
            var entries = new List<ThorEntry>();

            int totalLen = data.Length;
            int pos = 0;

            // Detect layout variant by peeking first Int32
            bool variantB = false;
            if (totalLen >= 4)
            {
                int firstInt = BitConverter.ToInt32(data, 0);
                if (firstInt >= 0 && (long)firstInt * MinimalRecordSize + 4 <= totalLen)
                    variantB = true;
            }

            if (variantB)
            {
                if (totalLen < 4)
                    throw new InvalidDataException("missing entry count");
                int count = BitConverter.ToInt32(data, 0);
                pos = 4;
                for (int i = 0; i < count; i++)
                {
                    if (!ReadEntry(data, ref pos, totalLen, entries))
                        throw new InvalidDataException("unexpected end of entries");
                }
            }
            else
            {
                while (pos < totalLen)
                {
                    if (!ReadEntry(data, ref pos, totalLen, entries))
                        break; // trailing zeros allowed
                }
            }

            return entries;
        }

        private static bool ReadEntry(byte[] buffer, ref int pos, int totalLen, List<ThorEntry> entries)
        {
            int remaining = totalLen - pos;
            if (remaining < 8)
                return false; // trailing zeros allowed

            int rawPathLen = BitConverter.ToInt32(buffer, pos);
            int dataLen = BitConverter.ToInt32(buffer, pos + 4);

            int header = 8;
            int pathLen = rawPathLen;

            // Default interpretation
            bool success = pathLen >= 0 && dataLen >= 0 &&
                           (long)pos + header + pathLen + dataLen <= totalLen;

            // Fallbacks for negative lengths
            if (!success && (rawPathLen < 0 || dataLen < 0))
            {
                // Try with optional flags
                if (!success && remaining >= 12)
                {
                    header = 12;
                    pathLen = rawPathLen;
                    success = pathLen >= 0 && dataLen >= 0 &&
                              (long)pos + header + pathLen + dataLen <= totalLen;
                }

                // Try null-terminated path without flags
                if (!success && dataLen >= 0)
                {
                    header = 8;
                    int scanStart = pos + header;
                    int idx = scanStart;
                    while (idx < totalLen && buffer[idx] != 0) idx++;
                    if (idx < totalLen)
                    {
                        pathLen = idx - scanStart + 1; // include terminator
                        success = (long)pos + header + pathLen + dataLen <= totalLen;
                    }
                }

                // Try null-terminated path with flags
                if (!success && remaining >= 12 && dataLen >= 0)
                {
                    header = 12;
                    int scanStart = pos + header;
                    int idx = scanStart;
                    while (idx < totalLen && buffer[idx] != 0) idx++;
                    if (idx < totalLen)
                    {
                        pathLen = idx - scanStart + 1; // include terminator
                        success = (long)pos + header + pathLen + dataLen <= totalLen;
                    }
                }
            }

            if (!success)
                throw new InvalidDataException("entry exceeds remaining bytes");

            int cursor = pos + header;
            var pathBytes = new byte[pathLen];
            Buffer.BlockCopy(buffer, cursor, pathBytes, 0, pathLen);
            cursor += pathLen;

            // Drop terminating zero if present
            if (pathBytes.Length > 0 && pathBytes[pathBytes.Length - 1] == 0)
                Array.Resize(ref pathBytes, pathBytes.Length - 1);

            string path = DecodePath(pathBytes);
            path = NormalizePath(path);

            var fileData = new byte[Math.Max(0, dataLen)];
            if (dataLen > 0)
                Buffer.BlockCopy(buffer, cursor, fileData, 0, dataLen);

            pos = checked((int)((long)pos + header + pathLen + dataLen));

            if (!string.IsNullOrEmpty(path) && path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                entries.Add(new ThorEntry(path, fileData));

            return true;
        }

        private static string DecodePath(byte[] pathBytes)
        {
            Encoding[] encodings = new Encoding[]
            {
                Encoding.GetEncoding(949, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), // CP949
                Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), // Windows-1252
                new UTF8Encoding(false, true)
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

        private static int FindZlibHeader(byte[] data, int start)
        {
            for (int i = start; i < data.Length - 1; i++)
            {
                byte cmf = data[i];
                byte flg = data[i + 1];
                if (cmf != 0x78)
                    continue;
                if (flg != 0x01 && flg != 0x5E && flg != 0x9C && flg != 0xDA)
                    continue;
                int combined = (cmf << 8) | flg;
                if ((cmf & 0x0F) == 8 && combined % 31 == 0)
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
            while (br.BaseStream.Position < br.BaseStream.Length && (b = br.ReadByte()) != 0)
            {
                bytes.Add(b);
            }
            return bytes.Count > 0 ? Encoding.ASCII.GetString(bytes.ToArray()) : string.Empty;
        }

        private static string NormalizePath(string path)
        {
            var parts = path.Replace('\\', '/').Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
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

