using System;
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
            public PatchProgress(string phase, int index = 0, int count = 0, string path = null)
            {
                Phase = phase;
                Index = index;
                Count = count;
                Path = path;
            }

            public string Phase { get; }
            public int Index { get; }
            public int Count { get; }
            public string Path { get; }
        }

        /// <summary>
        /// Applies a Thor patch archive by merging its contents into the target GRF.
        /// The target GRF is taken from the archive metadata or defaults to "data.grf".
        /// </summary>
        /// <param name="thorFilePath">Path to the downloaded Thor archive.</param>
        /// <param name="progress">Optional progress reporter.</param>
        public static void ApplyPatch(string thorFilePath, IProgress<PatchProgress> progress = null)
        {
            if (!File.Exists(thorFilePath))
                throw new FileNotFoundException("Thor file not found", thorFilePath);

            if (!string.Equals(Path.GetExtension(thorFilePath), ".thor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Patch file is not a .thor archive.");

            var phaseProgress = new Progress<string>(msg =>
                progress?.Report(new PatchProgress(msg)));

            var archive = ThorArchive.Open(thorFilePath, phaseProgress);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string grfPath = Path.Combine(baseDir, archive.TargetGrf);
            Directory.CreateDirectory(Path.GetDirectoryName(grfPath));

            var grf = new SimpleGrf(grfPath);
            grf.Load();

            progress?.Report(new PatchProgress($"Merging {archive.Entries.Count} files"));

            int total = archive.Entries.Count;
            int index = 0;
            foreach (var entry in archive.Entries)
            {
                progress?.Report(new PatchProgress(null, ++index, total, entry.Path));
                grf.InsertOrReplace(entry.Path, entry.Data);
            }

            progress?.Report(new PatchProgress("Saving GRF"));
            grf.Save();
            // Verify by reloading the index
            grf.Load();
        }
    }
}

