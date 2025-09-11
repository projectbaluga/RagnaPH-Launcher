using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace RagnaPHPatcher
{
    public sealed class PatchEntry
    {
        public string Path;
        public byte[] Bytes;
        public bool IsDirectory => Bytes == null;
    }

    public static class SafeGrfPatcher
    {
        public static void ApplyThorPatchTransactional(string thorPath, IProgress<ThorPatcher.PatchProgress> progress = null)
        {
            if (!File.Exists(thorPath))
                throw new FileNotFoundException(".thor not found", thorPath);
            if (!string.Equals(Path.GetExtension(thorPath), ".thor", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Patch file is not a .thor archive.");

            progress?.Report(new ThorPatcher.PatchProgress("Dry-run"));

            var archive = ThorArchive.Open(thorPath);

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string grfPath = Path.Combine(baseDir, archive.TargetGrf);

            var entries = archive.Entries
                .Select(e => NormalizeEntry(new PatchEntry { Path = e.Path, Bytes = e.Data.Length == 0 ? null : e.Data }))
                .ToList();

            if (entries.Count == 0 || entries.All(e => e.IsDirectory))
                throw new InvalidDataException("No files found in .thor payload.");

            var grfEntries = entries
                .Where(e => e.Path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var fsEntries = entries
                .Where(e => !e.Path.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (grfEntries.Count > 0 && !File.Exists(grfPath))
                throw new FileNotFoundException("Base GRF not found", grfPath);

            long totalBytes = grfEntries.Where(e => !e.IsDirectory).Sum(e => (long)e.Bytes.Length);
            if (grfEntries.Count > 0)
                EnsureFreeSpace(grfPath, totalBytes + 32L * 1024 * 1024);

            string temp = grfPath + ".tmp";
            string bak = grfPath + ".bak";

            using var mutex = new Mutex(false, @"Global\RagnaPH.GRFPatch");
            if (!mutex.WaitOne(TimeSpan.FromSeconds(10)))
                throw new IOException("Another patch operation is in progress.");

            try
            {
                int total = grfEntries.Count(e => !e.IsDirectory) + fsEntries.Count(e => !e.IsDirectory);
                int index = 0;

                // Write file system entries first
                foreach (var entry in fsEntries)
                {
                    if (entry.IsDirectory)
                    {
                        Directory.CreateDirectory(Path.Combine(baseDir, entry.Path));
                        continue;
                    }
                    progress?.Report(new ThorPatcher.PatchProgress(null, ++index, total, entry.Path));
                    string outPath = Path.Combine(baseDir, entry.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    File.WriteAllBytes(outPath, entry.Bytes);
                }

                // Patch GRF entries into a temporary copy
                progress?.Report(new ThorPatcher.PatchProgress("Apply to temp"));

                File.Copy(grfPath, temp, overwrite: true);

                var grf = new SimpleGrf(temp);
                grf.Load();
                foreach (var entry in grfEntries)
                {
                    if (entry.IsDirectory) continue;
                    progress?.Report(new ThorPatcher.PatchProgress(null, ++index, total, entry.Path));
                    grf.InsertOrReplace(entry.Path, entry.Bytes);
                }
                grf.Save();

                progress?.Report(new ThorPatcher.PatchProgress("Validate"));
                if (!Validate(temp, grfEntries))
                    throw new InvalidDataException("Validation failed after writing temp GRF.");

                progress?.Report(new ThorPatcher.PatchProgress("Swap"));
                File.Replace(temp, grfPath, bak);

                progress?.Report(new ThorPatcher.PatchProgress("Done"));
                try { File.Delete(thorPath); } catch { }
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }

        private static PatchEntry NormalizeEntry(PatchEntry e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var p = e.Path?.Replace('\\', '/').Trim();
            if (string.IsNullOrWhiteSpace(p)) throw new InvalidDataException("Empty entry path.");

            var safe = string.Join("/", p.Split('/').Where(s => s != "." && s != ".."));
            safe = safe.TrimStart('/');
            if (string.IsNullOrEmpty(safe))
                throw new InvalidDataException("Empty entry path.");

            e.Path = safe;
            return e;
        }

        private static void EnsureFreeSpace(string nearPath, long needBytes)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(nearPath))!;
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < needBytes)
                throw new IOException($"Not enough disk space (need ~{needBytes/1024/1024} MB).");
        }

        private static bool Validate(string grfTemp, IEnumerable<PatchEntry> files)
        {
            var grf = new SimpleGrf(grfTemp);
            grf.Load();
            foreach (var f in files.Where(x => !x.IsDirectory))
            {
                var read = grf.Read(f.Path);
                if (read == null || read.Length != f.Bytes.Length)
                    return false;
            }
            return grf.IsHeaderValid && grf.FileCount > 0;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}
