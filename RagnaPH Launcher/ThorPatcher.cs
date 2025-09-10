using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Applies a Thor patch archive by merging its contents into the specified GRF file.
        /// Paths are normalised to avoid escaping the GRF directory. The archive is left on disk so callers
        /// can decide whether to remove it (e.g. only after successful patching).
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

            var grf = new SimpleGrf(grfFilePath);
            grf.Load();

            using (var stream = File.OpenRead(thorFilePath))
            {
                // Validate THOR header before scanning for the embedded ZIP archive
                var header = new byte[4];
                if (stream.Read(header, 0, 4) != 4 || header[0] != 'T' || header[1] != 'H' || header[2] != 'O' || header[3] != 'R')
                    throw new InvalidDataException("Invalid THOR file header.");

                stream.Seek(0, SeekOrigin.Begin);
                long offset = FindZipOffset(stream);
                if (offset < 0)
                    throw new InvalidDataException("ZIP signature not found in THOR file.");

                stream.Seek(offset, SeekOrigin.Begin);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                {
                    string baseFull = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
                    foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        var rel = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var full = Path.GetFullPath(Path.Combine(baseDir, rel));

                        if (!full.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                            continue; // skip paths escaping the base dir

                        var grfPath = full.Substring(baseFull.Length)
                            .Replace(Path.DirectorySeparatorChar, '/');

                        using (var entryStream = entry.Open())
                        using (var ms = new MemoryStream())
                        {
                            entryStream.CopyTo(ms);
                            grf.InsertOrReplace(grfPath, ms.ToArray());
                        }
                    }
                }
            }

            grf.Save();
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
