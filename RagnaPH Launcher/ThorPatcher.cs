using System;
using System.IO;
using SharpCompress.Archives;

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
            if (!File.Exists(grfFilePath))
                throw new FileNotFoundException("GRF file not found", grfFilePath);

            var baseDir = Path.GetDirectoryName(grfFilePath);

            using (var archive = ArchiveFactory.Open(thorFilePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    var relativePath = entry.Key.Replace('/', Path.DirectorySeparatorChar);
                    var destinationPath = Path.Combine(baseDir, relativePath);
                    var destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!string.IsNullOrEmpty(destinationDir))
                        Directory.CreateDirectory(destinationDir);

                    using (var destStream = File.Open(destinationPath, FileMode.Create, FileAccess.Write))
                    {
                        entry.WriteTo(destStream);
                    }
                }
            }

            File.Delete(thorFilePath);
        }
    }
}
