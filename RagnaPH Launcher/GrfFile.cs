using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RagnaPHPatcher
{
    internal class GrfFile
    {
        private readonly string _path;
        private readonly Dictionary<string, FileEntry> _entries = new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        private class FileEntry
        {
            public string Path;
            public byte[] Data;
        }

        public GrfFile(string path)
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
                var magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (magic != "GRF2")
                    throw new InvalidDataException("Invalid GRF header");

                br.ReadBytes(8); // reserved
                int count = br.ReadInt32();

                for (int i = 0; i < count; i++)
                {
                    int nameLen = br.ReadInt32();
                    string path = Encoding.Unicode.GetString(br.ReadBytes(nameLen * 2));
                    path = path.Replace('/', '\\');
                    int dataLen = br.ReadInt32();
                    byte[] data = br.ReadBytes(dataLen);
                    _entries[path.ToLowerInvariant()] = new FileEntry { Path = path, Data = data };
                }
            }
        }

        public void InsertOrReplace(string path, byte[] data)
        {
            _entries[path.ToLowerInvariant()] = new FileEntry { Path = path, Data = data };
        }

        public void Save(bool inPlace = true)
        {
            string outPath = _path;
            string tempPath = _path + ".tmp";
            if (!inPlace)
                outPath = tempPath;

            using (var fs = File.Create(outPath))
            using (var bw = new BinaryWriter(fs, Encoding.Unicode))
            {
                bw.Write(Encoding.ASCII.GetBytes("GRF2"));
                bw.Write(new byte[8]);
                bw.Write(_entries.Count);
                foreach (var kv in _entries)
                {
                    bw.Write(kv.Value.Path.Length);
                    bw.Write(Encoding.Unicode.GetBytes(kv.Value.Path));
                    bw.Write(kv.Value.Data.Length);
                    bw.Write(kv.Value.Data);
                }
            }

            if (!inPlace)
            {
                File.Copy(tempPath, _path, true);
                File.Delete(tempPath);
            }
        }
    }
}
