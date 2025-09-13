using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Checksum;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class ThorArchiveTests
{
    private enum DataOffsetBase { HeaderEnd, Absolute, TableEnd }
    [Fact]
    public void Open_InvalidFile_ThrowsFriendlyMessage()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a thor");
            var ex = Assert.Throws<InvalidDataException>(() => ThorArchive.Open(path));
            Assert.Contains("THOR: BAD_HEADER", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadEntries_ValidThor_ReturnsFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "hello.txt", "hello");
            using var archive = ThorArchive.Open(path);
            var entry = Assert.Single(archive.Entries);
            using var stream = await archive.OpenEntryStreamAsync(entry);
            using var sr = new StreamReader(stream);
            Assert.Equal("hello", await sr.ReadToEndAsync());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadEntries_LengthPrefixed_ReturnsFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "hello.txt", "hi", lengthPrefixedPath: true);
            using var archive = ThorArchive.Open(path);
            Assert.Single(archive.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task OpenEntry_CrcMismatch_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "hello.txt", "hello", corruptCrc: true);
            using var archive = ThorArchive.Open(path);
            var entry = Assert.Single(archive.Entries);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                using var stream = await archive.OpenEntryStreamAsync(entry);
            });
            Assert.Contains("THOR: BAD_CRC hello.txt", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_LenientOffset_Passes()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "ok.txt", "hi", overrideTableOffset: 0);
            using var archive = ThorArchive.Open(path);
            Assert.Single(archive.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadEntries_AbsoluteOffset_Passes()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "abs.txt", "hi", dataOffsetBase: DataOffsetBase.Absolute);
            using var archive = ThorArchive.Open(path);
            Assert.Single(archive.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadEntries_TableEndOffset_Passes()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "after.txt", "yo", dataOffsetBase: DataOffsetBase.TableEnd);
            using var archive = ThorArchive.Open(path);
            Assert.Single(archive.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Open_TruncatedTable_Throws()
    {
        var path = Path.GetTempFileName();
        try
        {
            CreateSimpleThor(path, "oops.txt", "oops");
            using (var fs = new FileStream(path, FileMode.Open))
            {
                fs.SetLength(fs.Length - 1);
            }
            var ex = Assert.Throws<InvalidDataException>(() => ThorArchive.Open(path));
            Assert.Contains("THOR: BAD_TABLE", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateSimpleThor(string path, string fileName, string content, bool corruptCrc = false, int? overrideTableOffset = null, bool lengthPrefixedPath = false, DataOffsetBase dataOffsetBase = DataOffsetBase.HeaderEnd)
    {
        var fileData = Encoding.UTF8.GetBytes(content);
        var compressedFile = CompressZlib(fileData);
        var crc32 = new Crc32();
        crc32.Update(fileData);
        uint crc = (uint)crc32.Value;
        if (corruptCrc)
            crc++;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.ASCII, leaveOpen: true);
        bw.Write(Encoding.ASCII.GetBytes("ASSF (C) 2007 Aeomin DEV"));
        bw.Write((byte)1); // version
        bw.Write(1); // file count
        bw.Write((short)0x30); // mode
        bw.Write((byte)0); // target len
        long lenPos = fs.Position;
        bw.Write(0); // table length placeholder
        long offPos = fs.Position;
        bw.Write(0); // table offset placeholder
        if (dataOffsetBase == DataOffsetBase.TableEnd)
        {
            long tableOffset = fs.Position;
            using var tableMs = new MemoryStream();
            using (var tableBw = new BinaryWriter(tableMs, Encoding.UTF8, leaveOpen: true))
            {
                if (lengthPrefixedPath)
                {
                    var nameBytes = Encoding.UTF8.GetBytes(fileName);
                    tableBw.Write((ushort)nameBytes.Length);
                    tableBw.Write(nameBytes);
                }
                else
                {
                    tableBw.Write(Encoding.UTF8.GetBytes(fileName));
                    tableBw.Write((byte)0); // NUL terminator
                }
                tableBw.Write((uint)compressedFile.Length); // compSize
                tableBw.Write((uint)fileData.Length);       // uncompSize
                tableBw.Write((uint)0);                     // dataOffset relative to table end
                tableBw.Write(crc);                         // crc32
                tableBw.Write((byte)0);                     // flags
                while (tableMs.Position % 4 != 0)
                    tableBw.Write((byte)0);
            }
            var tableCompressed = CompressZlib(tableMs.ToArray());
            bw.Write(tableCompressed);
            long tableLen = tableCompressed.Length;
            bw.Write(compressedFile); // data after table
            bw.Flush();
            fs.Position = lenPos;
            bw.Write((int)tableLen);
            fs.Position = offPos;
            bw.Write((int)tableOffset);
            return;
        }

        long dataOffset = fs.Position;
        bw.Write(compressedFile); // file data before table
        long tableOffset = fs.Position;
        using var tableMs2 = new MemoryStream();
        using (var tableBw = new BinaryWriter(tableMs2, Encoding.UTF8, leaveOpen: true))
        {
            if (lengthPrefixedPath)
            {
                var nameBytes = Encoding.UTF8.GetBytes(fileName);
                tableBw.Write((ushort)nameBytes.Length);
                tableBw.Write(nameBytes);
            }
            else
            {
                tableBw.Write(Encoding.UTF8.GetBytes(fileName));
                tableBw.Write((byte)0); // NUL terminator
            }
            tableBw.Write((uint)compressedFile.Length); // compSize
            tableBw.Write((uint)fileData.Length);       // uncompSize
            uint storedOffset = dataOffsetBase switch
            {
                DataOffsetBase.Absolute => (uint)dataOffset,
                _ => 0u,
            };
            tableBw.Write(storedOffset);                // dataOffset
            tableBw.Write(crc);                         // crc32
            tableBw.Write((byte)0);                     // flags
            while (tableMs2.Position % 4 != 0)
                tableBw.Write((byte)0);
        }
        var tableCompressed2 = CompressZlib(tableMs2.ToArray());
        bw.Write(tableCompressed2);
        bw.Flush();
        long tableLen2 = tableCompressed2.Length;
        fs.Position = lenPos;
        bw.Write((int)tableLen2);
        fs.Position = offPos;
        bw.Write(overrideTableOffset ?? (int)tableOffset);
    }

    private static byte[] CompressZlib(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78); ms.WriteByte(0x9C);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
        {
            ds.Write(data, 0, data.Length);
        }
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >> 8));
        ms.WriteByte((byte)adler);
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        const uint ModAdler = 65521;
        uint a = 1, b = 0;
        foreach (byte d in data)
        {
            a = (a + d) % ModAdler;
            b = (b + a) % ModAdler;
        }
        return (b << 16) | a;
    }
}

