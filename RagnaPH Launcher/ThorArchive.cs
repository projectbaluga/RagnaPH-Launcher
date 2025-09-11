using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    /// <summary>
    /// Thor archive reader for the ASSF format used by traditional Ragnarok patchers.
    /// The file layout is:
    ///
    /// ["ASSF" magic][payloadOffset:int][payloadSize:int]
    ///   [other header data ...][payload:byte[payloadSize]]
    ///     payload contains:
    ///       ["ASSF" magic][compressedSize:int][uncompressedSize:int]
    ///         [zlibData:byte[compressedSize]]  --> decompresses to:
    ///           [entryCount:int]
    ///             repeated entryCount times:
    ///               [target:byte] (0 = GRF, 1 = file system)
    ///               [pathLen:int][path:utf8]
    ///               [dataLen:int][data:byte[dataLen]]
    ///
    /// The entire entries block is zlib-compressed.  Paths are stored as UTF-8 strings
    /// inside the compressed section.
    /// </summary>
    internal sealed class ThorArchive
    {
        internal sealed class ThorEntry
        {
            public ThorEntry(bool targetIsGrf, string path, byte[] data)
            {
                TargetIsGrf = targetIsGrf;
                Path = path;
                Data = data;
            }

            public bool TargetIsGrf { get; }
            public string Path { get; }
            public byte[] Data { get; }
        }

        public string TargetGrf { get; }
        public byte PatchMode { get; }
        public IReadOnlyList<ThorEntry> Entries { get; }

        private ThorArchive(string targetGrf, byte patchMode, List<ThorEntry> entries)
        {
            TargetGrf = targetGrf;
            PatchMode = patchMode;
            Entries = entries;
        }

        public static ThorArchive Open(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true))
            {
                var headerMagic = br.ReadBytes(4);
                if (headerMagic.Length != 4 || headerMagic[0] != 'A' || headerMagic[1] != 'S' ||
                    headerMagic[2] != 'S' || headerMagic[3] != 'F')
                    throw new InvalidDataException("Invalid .thor file header.");

                // Read payload location and length stored as little-endian Int32 values.
                int payloadOffset, payloadSize;
                try
                {
                    payloadOffset = br.ReadInt32();
                    payloadSize = br.ReadInt32();
                }
                catch (EndOfStreamException ex)
                {
                    throw new InvalidDataException("Incomplete .thor file header.", ex);
                }

                long payloadEnd = (long)payloadOffset + payloadSize;
                if (payloadOffset < 0 || payloadSize < 0 || payloadEnd > fs.Length)
                    throw new InvalidDataException("Invalid .thor payload region.");

                // Extract metadata between the header and payload so we can expose
                // information such as the intended GRF target and patch mode.  The
                // layout of this section varies between tools but is always located
                // immediately before the payload.
                string targetGrf = string.Empty;
                byte patchMode = 0;
                int metaLength = payloadOffset - (int)fs.Position;
                if (metaLength < 0)
                    throw new InvalidDataException("Invalid .thor header region.");
                if (metaLength > 0)
                {
                    var metaBytes = br.ReadBytes(metaLength);
                    using (var metaMs = new MemoryStream(metaBytes))
                    using (var metaBr = new BinaryReader(metaMs, Encoding.ASCII))
                    {
                        var sb = new List<byte>();
                        while (metaMs.Position < metaMs.Length)
                        {
                            byte b = metaBr.ReadByte();
                            if (b == 0)
                                break;
                            sb.Add(b);
                        }
                        if (sb.Count > 0)
                            targetGrf = Encoding.ASCII.GetString(sb.ToArray());
                        if (metaMs.Position < metaMs.Length)
                            patchMode = metaBr.ReadByte();
                    }
                }

                // Read only the specified payload slice before decompressing.
                fs.Position = payloadOffset;
                var payload = br.ReadBytes(payloadSize);
                if (payload.Length != payloadSize)
                    throw new InvalidDataException("Incomplete .thor payload region.");

                using (var payloadMs = new MemoryStream(payload))
                using (var payloadBr = new BinaryReader(payloadMs, Encoding.ASCII))
                {
                    var magic = payloadBr.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'S' || magic[2] != 'S' || magic[3] != 'F')
                        throw new InvalidDataException("Invalid ASSF payload header.");

                    int compressedSize = payloadBr.ReadInt32();
                    int uncompressedSize = payloadBr.ReadInt32();
                    long compressedEnd = payloadBr.BaseStream.Position + compressedSize;
                    if (compressedSize < 0 || uncompressedSize < 0 || compressedEnd > payloadBr.BaseStream.Length)
                        throw new InvalidDataException("Invalid ASSF payload region.");

                    var compressed = payloadBr.ReadBytes(compressedSize);
                    if (compressed.Length != compressedSize)
                        throw new InvalidDataException("Incomplete ASSF payload region.");

                    var decompressed = DecompressZlib(compressed, uncompressedSize);

                    using (var ms = new MemoryStream(decompressed))
                    using (var br2 = new BinaryReader(ms, Encoding.UTF8))
                    {
                        int count = br2.ReadInt32();
                        var entries = new List<ThorEntry>(count);
                        for (int i = 0; i < count; i++)
                        {
                            try
                            {
                                byte target = br2.ReadByte();
                                int pathLen = br2.ReadInt32();
                                var pathBytes = br2.ReadBytes(pathLen);
                                string entryPath = Encoding.UTF8.GetString(pathBytes);
                                int dataLen = br2.ReadInt32();
                                byte[] data = br2.ReadBytes(dataLen);
                                entries.Add(new ThorEntry(target == 0, entryPath, data));
                            }
                            catch (EndOfStreamException ex)
                            {
                                throw new InvalidDataException("Incomplete THOR archive.", ex);
                            }
                        }

                        return new ThorArchive(targetGrf, patchMode, entries);
                    }
                }
            }
        }

        private static byte[] DecompressZlib(byte[] data, int expectedSize)
        {
            // Zlib streams start with a two byte header and end with a four byte Adler32 checksum.
            if (data.Length < 6)
                throw new InvalidDataException("Incomplete zlib payload.");

            using (var ms = new MemoryStream(data, 2, data.Length - 6))
            {
                try
                {
                    using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (var outMs = new MemoryStream(expectedSize > 0 ? expectedSize : 0))
                    {
                        ds.CopyTo(outMs);
                        return outMs.ToArray();
                    }
                }
                catch (InvalidDataException ex)
                {
                    // Surface decompression failures to the caller so that invalid archives
                    // are rejected rather than partially processed.
                    throw new InvalidDataException("Failed to decompress THOR archive payload.", ex);
                }
            }
        }
    }
}
