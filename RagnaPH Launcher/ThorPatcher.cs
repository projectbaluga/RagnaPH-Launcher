using System;
using System.Collections.Generic;
using System.IO;

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

            // Ensure we are dealing with a thor archive.  This mirrors the check done by
            // the command-line entry point so that callers using the library directly
            // also benefit from the validation.
            if (!string.Equals(Path.GetExtension(thorFilePath), ".thor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Patch file is not a .thor archive.");

            if (string.IsNullOrWhiteSpace(grfFilePath) ||
                string.Equals(Path.GetExtension(grfFilePath), ".thor", StringComparison.OrdinalIgnoreCase))
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
    }
}

