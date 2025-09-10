using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

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
                    File.WriteAllBytes(outPath, entry.Data);
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
        /// Thor archive reader for the custom THOR format used by Ragnarok patchers. The file layout is :
        ///
        /// ["THOR" magic][entryCount]
        ///   repeated entryCount times:
        ///     [target:byte] (0 = GRF, 1 = file system)
        ///     [pathLen:int][path:utf16]
        ///     [grfLen:int][grf:utf16]  (optional; empty means default GRF)
        ///     [isCompressed:byte]
        ///     [dataLen:int][data:byte[]]
        ///
        /// Data blocks may be zlib-compressed.  Paths are stored as UTF-16LE strings.
        /// </summary>
        private sealed class ThorArchive
        {
            internal sealed class ThorEntry
            {
                public ThorEntry(bool targetIsGrf, string path, string grf, byte[] data)
                {
                    TargetIsGrf = targetIsGrf;
                    Path = path;
                    Grf = grf;
                    Data = data;
                }

                public bool TargetIsGrf { get; }
                public string Path { get; }
                public string Grf { get; }
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
                using (var br = new BinaryReader(fs, System.Text.Encoding.Unicode))
                {
                    var magic = br.ReadBytes(4);
                    if (magic.Length != 4 || magic[0] != 'T' || magic[1] != 'H' || magic[2] != 'O' || magic[3] != 'R')
                        throw new InvalidDataException("Invalid THOR file header.");

                    int count = br.ReadInt32();
                    var entries = new List<ThorEntry>(count);
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            byte target = br.ReadByte();
                            int pathLen = br.ReadInt32();
                            string entryPath = new string(br.ReadChars(pathLen));
                            int grfLen = br.ReadInt32();
                            string grfName = grfLen > 0 ? new string(br.ReadChars(grfLen)) : string.Empty;
                            byte compressed = br.ReadByte();
                            int dataLen = br.ReadInt32();
                            byte[] data = br.ReadBytes(dataLen);
                            if (compressed != 0)
                                data = DecompressZlib(data);
                            entries.Add(new ThorEntry(target == 0, entryPath, grfName, data));
                        }
                        catch (EndOfStreamException ex)
                        {
                            throw new InvalidDataException("Incomplete THOR archive.", ex);
                        }
                    }

                    return new ThorArchive(entries);
                }
            }

            private static byte[] DecompressZlib(byte[] data)
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
                        using (var outMs = new MemoryStream())
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

