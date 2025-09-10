using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

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
        /// Applies a Thor patch archive by merging its contents into the specified GRF file.
        /// The patch archive is validated and each file path is normalised to prevent path traversal.
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
            grf.Load();

            var archive = ThorArchive.Open(thorFilePath);
            int total = archive.Entries.Count;
            for (int i = 0; i < total; i++)
            {
                var entry = archive.Entries[i];
                var path = NormalizePath(entry.Path);
                if (string.IsNullOrEmpty(path))
                    continue;

                progress?.Report(new PatchProgress(i + 1, total, path));
                grf.InsertOrReplace(path, entry.Data);
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
            return string.Join("/", stack.Reverse());
        }

        /// <summary>
        /// Minimal Thor archive reader. Thor files contain a small header followed by a standard ZIP archive.
        /// </summary>
        private sealed class ThorArchive
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

            public IReadOnlyList<ThorEntry> Entries { get; }

            private ThorArchive(List<ThorEntry> entries)
            {
                Entries = entries;
            }

            public static ThorArchive Open(string path)
            {
                using (var stream = File.OpenRead(path))
                {
                    var header = new byte[4];
                    if (stream.Read(header, 0, 4) != 4 || header[0] != 'T' || header[1] != 'H' || header[2] != 'O' || header[3] != 'R')
                        throw new InvalidDataException("Invalid THOR file header.");

                    stream.Seek(0, SeekOrigin.Begin);
                    long offset = FindZipOffset(stream);
                    if (offset < 0)
                        throw new InvalidDataException("ZIP signature not found in THOR file.");

                    stream.Seek(offset, SeekOrigin.Begin);
                    try
                    {
                        using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
                        {
                            var entries = new List<ThorEntry>();
                            foreach (var e in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                            {
                                using (var es = e.Open())
                                using (var ms = new MemoryStream())
                                {
                                    es.CopyTo(ms);
                                    entries.Add(new ThorEntry(e.FullName, ms.ToArray()));
                                }
                            }
                            return new ThorArchive(entries);
                        }
                    }
                    catch (InvalidDataException ex)
                    {
                        throw new InvalidDataException("Corrupted THOR archive.", ex);
                    }
                }
            }

            private static long FindZipOffset(Stream stream)
            {
                const uint signature = 0x04034b50; // PK\x03\x04
                var buffer = new byte[4];
                while (stream.Read(buffer, 0, 4) == 4)
                {
                    uint current = (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16) | (buffer[3] << 24));
                    if (current == signature)
                        return stream.Position - 4;
                    stream.Seek(-3, SeekOrigin.Current);
                }
                return -1;
            }
        }
    }
}
