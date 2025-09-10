using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        /// <summary>
        /// Applies a Thor patch archive by extracting its contents alongside the specified GRF file.
        /// Files placed next to the GRF override existing game resources. The Thor archive is deleted after extraction.
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

            using (var stream = File.OpenRead(thorFilePath))
            {
                long offset = FindZipOffset(stream);
                if (offset < 0)
                    throw new InvalidDataException("ZIP signature not found in THOR file.");

                stream.Seek(offset, SeekOrigin.Begin);
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                {
                    foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
                    {
                        var relativePath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                        var destinationPath = Path.Combine(baseDir, relativePath);
                        var fullPath = Path.GetFullPath(destinationPath);

                        if (!fullPath.StartsWith(Path.GetFullPath(baseDir)))
                            continue;

                        var destinationDir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(destinationDir))
                            Directory.CreateDirectory(destinationDir);

                        using (var entryStream = entry.Open())
                        using (var fileStream = File.Create(fullPath))
                        {
                            entryStream.CopyTo(fileStream);
                        }
                    }
                }
            }

            File.Delete(thorFilePath);
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
