using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ICSharpCode.SharpZipLib.Zip.Compression;
using ICSharpCode.SharpZipLib.Zip.Compression.Streams;

namespace RagnaPH.Patching {
    public sealed class ThorFormatException : Exception {
        public ThorFormatException(string msg) : base(msg) { }
    }

    public sealed class ThorEntry {
        public string Path;
        public uint CompSize, UncompSize, DataOffset, Crc32;
        public ThorEntry(string path, uint comp, uint uncomp, uint off, uint crc) {
            Path = path; CompSize = comp; UncompSize = uncomp; DataOffset = off; Crc32 = crc;
        }
    }

    public static class ThorReaderFix {
        public static (IReadOnlyList<ThorEntry> entries, long dataBase, long tableOff, long tableSize)
        ReadThorStructure(string thorPath) {
            using var fs = File.OpenRead(thorPath);
            using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

            // --- Header (after magic/version if your code reads them elsewhere) ---
            // Move fs.Position to where your table numbers begin.
            long headerEnd = fs.Position;
            uint fileCount = br.ReadUInt32();
            uint tableSize = br.ReadUInt32();
            uint tableOffRaw = br.ReadUInt32();

            long fileLen = fs.Length;
            long tsize = tableSize;
            long toff = (tableOffRaw + tableSize == fileLen) ? tableOffRaw : fileLen - tsize;
            if (toff < headerEnd || toff + tsize != fileLen)
                throw new ThorFormatException("THOR: BAD_TABLE (offset/size)");

            // --- Table buffer (zlib auto) ---
            fs.Position = toff;
            byte[] raw = br.ReadBytes(checked((int)tsize));
            if (raw.Length != tsize) throw new ThorFormatException("THOR: BAD_TABLE (truncated table)");

            byte[] table = LooksZlib(raw) ? Zlib(raw) : raw;
            var entries = ParseEntries(table, (int)fileCount);

            // --- Choose data base (prefer data-before-table) ---
            long[] bases = (toff + tsize == fileLen)
                ? new[] { 0L, headerEnd }                  // typical: data before table
                : new[] { 0L, headerEnd, toff + tsize };   // rare: data after table

            long dataBase = ChooseDataBase(bases, entries, toff, fileLen);
            // --- Infer payload spans from next offsets ---
            InferCompSizesFromGaps(dataBase, toff, fileLen, entries);

            return (entries, dataBase, toff, tsize);
        }

        static bool LooksZlib(byte[] b) =>
            b.Length >= 2 && b[0] == 0x78 && (b[1] == 0x01 || b[1] == 0x9C || b[1] == 0xDA);

        static byte[] Zlib(byte[] src) {
            using var ms = new MemoryStream(src);
            using var z = new InflaterInputStream(ms);
            using var outMs = new MemoryStream();
            z.CopyTo(outMs);
            return outMs.ToArray();
        }

        static List<ThorEntry> ParseEntries(byte[] tb, int expectedCount) {
            var list = new List<ThorEntry>(Math.Max(1, expectedCount));
                         int pos = 0;
            for (int i = 0; i < expectedCount; i++) {
                string path = ReadPath(tb, ref pos);               // UTF-8 C-string or len-prefixed
                if (pos + 16 > tb.Length) throw new ThorFormatException("THOR: BAD_TABLE (entry fields OOB)");
                uint comp = BitConverter.ToUInt32(tb, pos + 0);
                uint uncomp = BitConverter.ToUInt32(tb, pos + 4);
                uint off = BitConverter.ToUInt32(tb, pos + 8);
                uint crc = BitConverter.ToUInt32(tb, pos + 12);
                pos += 16;

                // optional 1-byte flags then 4-byte align
                int pad = (4 - (pos % 4)) & 3;
                if (pad == 3 && pos < tb.Length) { pos += 1; pad = (4 - (pos % 4)) & 3; }
                pos = Math.Min(tb.Length, pos + pad);

                list.Add(new ThorEntry(path, comp, uncomp, off, crc));
            }
            return list;
        }

        static string ReadPath(byte[] s, ref int pos) {
            // try C-string
            int start = pos, end = pos;
            while (end < s.Length && s[end] != 0) end++;
            if (end < s.Length && s[end] == 0) {
                string path = Encoding.UTF8.GetString(s, start, end - start);
                pos = end + 1;
                return path.Replace('\\', '/');
            }
            // fallback: UInt16 length-prefixed
            if (pos + 2 > s.Length) throw new ThorFormatException("THOR: BAD_TABLE (name length OOB)");
            ushort n = BitConverter.ToUInt16(s, pos);
            pos += 2;
            if (pos + n > s.Length) throw new ThorFormatException("THOR: BAD_TABLE (name bytes OOB)");
            string p2 = Encoding.UTF8.GetString(s, pos, n);
            pos += n;
            return p2.Replace('\\', '/');
        }

        static long ChooseDataBase(long[] candidates, List<ThorEntry> entries, long tableOff, long fileLen) {
            foreach (var b in candidates) {
                if (b < 0) continue;
                bool before = (b != tableOff + (fileLen - (tableOff)));
                bool ok = true;
                foreach (var e in entries) {
                    long s = checked(b + (long)e.DataOffset);
                    if (before) { if (s < b || s >= tableOff) { ok = false; break; } }
                    else        { if (s < b || s >= fileLen) { ok = false; break; } }
                }
                if (ok) return b;
            }
            throw new ThorFormatException("THOR: BAD_TABLE (no valid data base)");
        }

        static void InferCompSizesFromGaps(long dataBase, long tableOff, long fileLen, List<ThorEntry> entries) {
            bool before = (dataBase != tableOff + (fileLen - tableOff));
            long bound = before ? tableOff : fileLen;
            var sorted = entries.Select((e, i) => new { e, i }).OrderBy(x => x.e.DataOffset).ToList();

            for (int k = 0; k < sorted.Count; k++) {
                var e = sorted[k].e;
                long start = checked(dataBase + (long)e.DataOffset);
                long nextStart = (k + 1 < sorted.Count)
                    ? checked(dataBase + (long)sorted[k + 1].e.DataOffset)
                    : bound;
                if (start < dataBase || start >= nextStart)
                    throw new ThorFormatException($"THOR: BAD_TABLE OOB entry {sorted[k].i} {e.Path}");

                long maxSpan = nextStart - start;
                long declared = e.CompSize;
                long use = (declared > 0 && declared <= maxSpan) ? declared : maxSpan;
                if (use <= 0) throw new ThorFormatException($"THOR: BAD_TABLE truncated entry {sorted[k].i}");
                e.CompSize = (uint)use; // persist inferred size
            }
        }
    }
}

