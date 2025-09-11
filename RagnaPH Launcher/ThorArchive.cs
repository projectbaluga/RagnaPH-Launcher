using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

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

                // Immediately after metadata: payloadOffset and payloadSize
                int payloadOffset;
                int payloadSize;
                try
                {
                    payloadOffset = br.ReadInt32();
                    payloadSize = br.ReadInt32();
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException("Incomplete .thor header region.", ex);
                }

                progress?.Report("Validating payload");

                int metaEnd = (int)br.BaseStream.Position;
                bool regionValid = payloadOffset >= 0 && payloadSize > 0 &&
                                   payloadOffset + payloadSize <= fileBytes.Length;
                if (!regionValid)
                {
                    // Attempt fallback scanning for a zlib header after the metadata region
                    payloadOffset = FindZlibHeader(fileBytes, metaEnd);
                    if (payloadOffset < 0)
                        throw new InvalidDataException("zlib header not found");
                    payloadSize = fileBytes.Length - payloadOffset;
                }

                if (payloadOffset < 0 || payloadSize <= 0 || payloadOffset + payloadSize > fileBytes.Length)
                    throw new InvalidDataException("payloadOffset/payloadSize out of bounds");

                // Slice the payload and decompress
                progress?.Report("Decompressing");

                var payload = new byte[payloadSize];
                Buffer.BlockCopy(fileBytes, payloadOffset, payload, 0, payloadSize);

                byte[] decompressed = DecompressPayload(payload);

                var entries = ParseEntries(decompressed);
                return new ThorArchive(targetGrf, patchMode, entries);
            }
        }

        private static List<ThorEntry> ParseEntries(byte[] data)
        {
            var entries = new List<ThorEntry>();
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true))
            {
                while (ms.Position < ms.Length)
                {
                    int pathLen, dataLen;
                    try
                    {
                        pathLen = br.ReadInt32();
                        dataLen = br.ReadInt32();
                    }
                    catch (EndOfStreamException ex)
                    {
                        throw new InvalidDataException("entry parse failed", ex);
                    }

                    if (pathLen <= 0 || dataLen < 0)
                        throw new InvalidDataException("entry parse failed");

                    // Optional CRC/flags
                    long afterLengths = ms.Position;
                    if (afterLengths + 4 + pathLen + dataLen <= ms.Length)
                        br.ReadInt32();
                    else
                        ms.Position = afterLengths;

                    if (ms.Position + pathLen + dataLen > ms.Length)
                        throw new InvalidDataException("entry parse failed");

                    var pathBytes = br.ReadBytes(pathLen);
                    string path;
                    try
                    {
                        path = Encoding.UTF8.GetString(pathBytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        path = Encoding.GetEncoding(1252).GetString(pathBytes);
                    }

                    path = NormalizePath(path);
                    if (string.IsNullOrEmpty(path) || !path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("entry path invalid");

                    var fileData = br.ReadBytes(dataLen);
                    if (fileData.Length != dataLen)
                        throw new InvalidDataException("entry parse failed");

                    entries.Add(new ThorEntry(path, fileData));
                }
            }
            return entries;
        }

        private static byte[] DecompressPayload(byte[] payload)
        {
            // Try zlib first
            try
            {
                using (var ms = new MemoryStream(payload))
                using (var zlib = new InflaterInputStream(ms, new Inflater(false)))
                using (var outMs = new MemoryStream())
                {
                    zlib.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch
            {
                // Fallback to raw DEFLATE
            }

            try
            {
                using (var ms = new MemoryStream(payload))
                using (var ds = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionMode.Decompress))
                using (var outMs = new MemoryStream())
                {
                    ds.CopyTo(outMs);
                    return outMs.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("payload decompression failed", ex);
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

        private static int FindZlibHeader(byte[] buffer, int start)
        {
            for (int i = start; i < buffer.Length - 1; i++)
            {
                byte cmf = buffer[i];
                byte flg = buffer[i + 1];
                if (cmf == 0x78 && (((cmf << 8) | flg) % 31 == 0))
                    return i;
            }
            return -1;
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

