using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace RagnaPHPatcher
{
    public static class ThorPatcher
    {
        public static bool IsThorArchive(string path)
        {
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var head = new byte[4];
                    if (fs.Read(head, 0, 4) != 4)
                        return false;
                    if (Encoding.ASCII.GetString(head) != "THOR")
                        return false;
                    return FindZipOffset(fs) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Applies a Thor patch archive by merging its contents into the specified GRF file.
        /// </summary>
        /// <param name="thorFilePath">Path to the Thor archive.</param>
        /// <param name="grfFilePath">Path to the target GRF file.</param>
        /// <returns>True if patching succeeded.</returns>
        public static bool ApplyPatch(string thorFilePath, string grfFilePath)
        {
            if (!File.Exists(thorFilePath))
                throw new FileNotFoundException("Thor file not found", thorFilePath);

            var baseDir = Path.GetDirectoryName(grfFilePath);
            if (string.IsNullOrEmpty(baseDir))
                throw new ArgumentException("Invalid GRF path", nameof(grfFilePath));

            List<(string path, byte[] data)> entries = new List<(string, byte[])>();

            try
            {
                using (var stream = File.OpenRead(thorFilePath))
                {
                    var header = new byte[8];
                    if (stream.Read(header, 0, 8) != 8 || Encoding.ASCII.GetString(header, 0, 4) != "THOR")
                        throw new InvalidDataException("Invalid THOR header");

                    long offset = FindZipOffset(stream);
                    if (offset < 0)
                        throw new InvalidDataException("ZIP signature not found in THOR file.");

                    stream.Seek(offset, SeekOrigin.Begin);
                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name))
                                continue;

                            string rel = NormalizePath(entry.FullName);
                            if (rel == null)
                                continue;

                            using (var es = entry.Open())
                            using (var ms = new MemoryStream())
                            {
                                es.CopyTo(ms);
                                entries.Add((rel, ms.ToArray()));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Failed to parse THOR archive", ex);
            }

            try
            {
                var grf = new GrfFile(grfFilePath);
                grf.Load();
                foreach (var e in entries)
                    grf.InsertOrReplace(e.path, e.data);
                grf.Save();
                return true;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Failed to merge patch into GRF", ex);
            }
        }

        private static string NormalizePath(string path)
        {
            path = path.Replace('/', '\\');
            if (path.Contains(".."))
                return null;
            return path;
        }

        private static long FindZipOffset(Stream stream)
        {
            const uint signature = 0x04034b50; // PK\x03\x04
            var buffer = new byte[4];
            stream.Position = 0;
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
