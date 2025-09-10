using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RagnaPHPatcher
{
    internal class SimpleGrf
    {
        private readonly string _path;
        private readonly Dictionary<string, FileEntry> _entries = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        private class FileEntry
        {
            public string OriginalPath { get; set; } = string.Empty;
            public byte[] Data { get; set; } = Array.Empty<byte>();
        }

        public SimpleGrf(string path)
        {
            _path = path;
        }

        public void Load()
        {
            if (!File.Exists(_path))
                return;

            using (var fs = File.OpenRead(_path))
            using (var br = new BinaryReader(fs, Encoding.Unicode))
            {
                var magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'G' || magic[1] != 'R' || magic[2] != 'F' || magic[3] != '2')
                    throw new InvalidDataException("Invalid GRF file header.");

                br.ReadBytes(8); // reserved
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    int pathLen = br.ReadInt32();
                    var pathChars = br.ReadChars(pathLen);
                    int dataLen = br.ReadInt32();
                    var data = br.ReadBytes(dataLen);
                    var entryPath = new string(pathChars);
                    _entries[entryPath.ToLowerInvariant()] = new FileEntry { OriginalPath = entryPath, Data = data };
                }
            }
        }

        public void InsertOrReplace(string path, byte[] data)
        {
            _entries[path.ToLowerInvariant()] = new FileEntry { OriginalPath = path, Data = data };
        }

        public void Save()
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using (var fs = File.Create(_path))
            using (var bw = new BinaryWriter(fs, Encoding.Unicode))
            {
                bw.Write(new byte[] { (byte)'G', (byte)'R', (byte)'F', (byte)'2' });
                bw.Write(new byte[8]);
                bw.Write(_entries.Count);
                foreach (var entry in _entries.Values)
                {
                    bw.Write(entry.OriginalPath.Length);
                    bw.Write(entry.OriginalPath.ToCharArray());
                    bw.Write(entry.Data.Length);
                    bw.Write(entry.Data);
                }
            }
        }
    }
}
