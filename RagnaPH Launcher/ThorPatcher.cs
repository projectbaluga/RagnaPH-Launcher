using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Represents progress information for patching.
        /// </summary>
        public readonly struct PatchProgress
        {
            public PatchProgress(int index, int count, string path)
            {
                Index = index;
                Count = count;
                Path = path;
            }

            public int Index { get; }
            public int Count { get; }
            public string Path { get; }
        }

        /// <summary>
        /// Applies a Thor patch archive by merging its contents into the specified GRF file
        /// and writing any file-system targets next to the GRF.  The patch archive is
        /// validated and each file path is normalised to prevent path traversal.
        /// </summary>
        /// <param name="thorFilePath">Path to the downloaded Thor archive.</param>
        /// <param name="grfFilePath">Path to the client GRF file.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static void ApplyPatch(string thorFilePath, string grfFilePath, IProgress<PatchProgress> progress = null)
        {
            if (!File.Exists(thorFilePath))
                throw new FileNotFoundException("Thor file not found", thorFilePath);

            if (string.IsNullOrWhiteSpace(grfFilePath))
                throw new ArgumentException("Invalid GRF path", nameof(grfFilePath));

            var grfDirectory = Path.GetDirectoryName(Path.GetFullPath(grfFilePath));
            if (string.IsNullOrEmpty(grfDirectory))
                throw new ArgumentException("Invalid GRF path", nameof(grfFilePath));

            Directory.CreateDirectory(grfDirectory);

            var grf = new SimpleGrf(grfFilePath);
            // Validate GRF header before attempting to merge so that a bad GRF does not get
            // overwritten with an empty file.
            grf.Load();

            var archive = ThorArchive.Open(thorFilePath);
            int total = archive.Entries.Count;
            for (int i = 0; i < total; i++)
            {
                var entry = archive.Entries[i];
                var normalised = NormalizePath(entry.Path);
                if (string.IsNullOrEmpty(normalised))
                    continue;

                progress?.Report(new PatchProgress(i + 1, total, normalised));

                if (entry.TargetIsGrf)
                {
                    grf.InsertOrReplace(normalised, entry.Data);
                }
                else
                {
                    var outPath = Path.Combine(grfDirectory, normalised.Replace('/', Path.DirectorySeparatorChar));
                    var outDir = Path.GetDirectoryName(outPath);
                    if (!string.IsNullOrEmpty(outDir))
                        Directory.CreateDirectory(outDir);
                    var tempFile = outPath + ".tmp";
                    File.WriteAllBytes(tempFile, entry.Data);
                    File.Copy(tempFile, outPath, true);
                    try { File.Delete(tempFile); } catch { }
                }
            }

            grf.Save();
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
            return string.Join("/", arr);
        }

        /// <summary>
        /// Thor archive reader for the ASSF format used by traditional Ragnarok
        /// patchers.  The file layout is:
        ///
        /// ["ASSF" magic][compressedSize:int][uncompressedSize:int]
        ///   [zlibData:byte[compressedSize]]  --> decompresses to:
        ///     [entryCount:int]
        ///       repeated entryCount times:
        ///         [target:byte] (0 = GRF, 1 = file system)
        ///         [pathLen:int][path:utf8]
        ///         [dataLen:int][data:byte[dataLen]]
        ///
        /// The entire entries block is zlib-compressed.  Paths are stored as
        /// UTF-8 strings inside the compressed section.
        /// </summary>
        private sealed class ThorArchive
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
                using (var br = new BinaryReader(fs, Encoding.ASCII))
                {
                    var magic = br.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != 'A' || magic[1] != 'S' || magic[2] != 'S' || magic[3] != 'F')
                        throw new InvalidDataException("Invalid THOR file header.");

                    int compressedSize = br.ReadInt32();
                    int uncompressedSize = br.ReadInt32();
                    var compressed = br.ReadBytes(compressedSize);
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

            private static byte[] DecompressZlib(byte[] data, int expectedSize)
            {
                try
                {
                    using (var ms = new MemoryStream(data))
                    {
                        if (data.Length > 2 && data[0] == 0x78)
                        {
                            // Skip zlib header
                            ms.ReadByte();
                            ms.ReadByte();
                        }

                        using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
                        using (var outMs = new MemoryStream(expectedSize > 0 ? expectedSize : 0))
                        {
                            ds.CopyTo(outMs);
                            return outMs.ToArray();
                        }
                    }
                }
                catch (InvalidDataException)
                {
                    return data; // fallback to original bytes on failure
                }
            }
        }
    }
}

