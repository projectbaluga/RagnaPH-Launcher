using System;
using System.IO;
using SharpCompress.Archives;
using SharpCompress.Common;
using GRF;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Applies a Thor patch archive to the specified GRF file.
        /// </summary>
        /// <param name="thorFilePath">Path to the downloaded Thor archive.</param>
        /// <param name="grfFilePath">Path to the client GRF file to be patched.</param>
        public static void ApplyPatch(string thorFilePath, string grfFilePath)
        {
            if (!File.Exists(thorFilePath))
                throw new FileNotFoundException("Thor file not found", thorFilePath);
            if (!File.Exists(grfFilePath))
                throw new FileNotFoundException("GRF file not found", grfFilePath);

            using (var archive = ArchiveFactory.Open(thorFilePath))
            using (var grf = new GrfFile(grfFilePath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory)
                        continue;

                    using (var ms = new MemoryStream())
                    {
                        entry.WriteTo(ms);
                        grf.AddFile(entry.Key.Replace('/', '\\'), ms.ToArray());
                    }
                }

                grf.Save();
            }

            File.Delete(thorFilePath);
        }
    }
}
