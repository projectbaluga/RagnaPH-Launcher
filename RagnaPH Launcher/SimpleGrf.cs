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
        private bool _headerValid;

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
            _entries.Clear();
            _headerValid = false;
            if (!File.Exists(_path))
                return;

            using (var fs = File.OpenRead(_path))
            using (var br = new BinaryReader(fs, Encoding.Unicode))
            {
                // Validate the file magic.  Older clients may ship a GRF in a different
                // format which would previously throw and abort the patch.  Instead, treat
                // an unexpected header as an empty container so the patcher can rebuild it
                // using the simple format below.
                if (fs.Length < 16)
                    return;
                var magic = br.ReadBytes(4);
                if (magic.Length != 4 || magic[0] != 'G' || magic[1] != 'R' || magic[2] != 'F' || magic[3] != '2')
                    return;

                _headerValid = true;
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

        public byte[] Read(string path)
        {
            return _entries.TryGetValue(path.ToLowerInvariant(), out var entry) ? entry.Data : null;
        }

        public bool IsHeaderValid => _headerValid;

        public int FileCount => _entries.Count;

        public void Save()
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tempPath = _path + ".tmp";
            using (var fs = File.Create(tempPath))
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

            File.Copy(tempPath, _path, true);
            try { File.Delete(tempPath); } catch { }
        }
    }
}
