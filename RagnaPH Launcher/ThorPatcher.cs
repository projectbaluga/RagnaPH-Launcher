using System;
using System.IO;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Applies a Thor patch archive by extracting its contents alongside the specified GRF file.
        /// Files placed next to the GRF override existing game resources.
        /// </summary>
        /// <param name="thorFilePath">Path to the downloaded Thor archive.</param>
        /// <param name="grfFilePath">Path to the client GRF file. Its directory is used as the extraction target.</param>
        public static void ApplyPatch(string thorFilePath, string grfFilePath)
        {
            if (!File.Exists(thorFilePath))
                throw new FileNotFoundException("Thor file not found", thorFilePath);

            var baseDir = Path.GetDirectoryName(grfFilePath);
            if (string.IsNullOrEmpty(baseDir))
                throw new ArgumentException("Invalid GRF path", nameof(grfFilePath));

            Directory.CreateDirectory(baseDir);

            using (var archive = ArchiveFactory.Open(thorFilePath))
            {
                var options = new ExtractionOptions { ExtractFullPath = true, Overwrite = true };

                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    var relativePath = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var destinationPath = Path.Combine(baseDir, relativePath);
                    var fullPath = Path.GetFullPath(destinationPath);

                    if (!fullPath.StartsWith(Path.GetFullPath(baseDir)))
                        continue;

                    var destinationDir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    entry.WriteToFile(fullPath, options);
                }
            }

            File.Delete(thorFilePath);
        }
    }
}
