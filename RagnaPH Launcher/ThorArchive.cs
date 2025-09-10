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
    /// ["ASSF" magic][compressedSize:int][uncompressedSize:int]
    ///   [zlibData:byte[compressedSize]]  --> decompresses to:
    ///     [entryCount:int]
    ///       repeated entryCount times:
    ///         [target:byte] (0 = GRF, 1 = file system)
    ///         [pathLen:int][path:utf8]
    ///         [dataLen:int][data:byte[dataLen]]
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

        public IReadOnlyList<ThorEntry> Entries { get; }

        private ThorArchive(List<ThorEntry> entries)
        {
            Entries = entries;
        }

        public static ThorArchive Open(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: true))
            {
                var headerMagic = br.ReadBytes(4);
                if (headerMagic.Length != 4 || headerMagic[0] != 'T' || headerMagic[1] != 'H' || headerMagic[2] != 'O' || headerMagic[3] != 'R')
                    throw new InvalidDataException("Invalid THOR file header.");

                // The THOR header contains the offset and size of the zlib payload.
                int payloadOffset = br.ReadInt32();
                int payloadSize = br.ReadInt32();
                if (payloadOffset < 0 || payloadSize < 0 || payloadOffset + payloadSize > fs.Length)
                    throw new InvalidDataException("Invalid THOR payload region.");

                fs.Position = payloadOffset;
                var payload = br.ReadBytes(payloadSize);

                using (var payloadMs = new MemoryStream(payload))
                using (var payloadBr = new BinaryReader(payloadMs, Encoding.ASCII))
                {
                    var magic = payloadBr.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'S' || magic[2] != 'S' || magic[3] != 'F')
                        throw new InvalidDataException("Invalid THOR payload header.");

                    int compressedSize = payloadBr.ReadInt32();
                    int uncompressedSize = payloadBr.ReadInt32();
                    var compressed = payloadBr.ReadBytes(compressedSize);
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

                        return new ThorArchive(entries);
                    }
                }
            }
        }

        private static byte[] DecompressZlib(byte[] data, int expectedSize)
        {
            using (var ms = new MemoryStream(data))
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
