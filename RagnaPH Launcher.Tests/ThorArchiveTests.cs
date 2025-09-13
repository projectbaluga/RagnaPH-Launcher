using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Checksums;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class ThorArchiveTests
{
    [Fact]
    public void Open_InvalidFile_ThrowsFriendlyMessage()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a thor");
            var ex = Assert.Throws<InvalidDataException>(() => ThorArchive.Open(path));
            Assert.Contains("Bad THOR header", ex.Message);
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
            Assert.Contains("Payload corruption", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void CreateSimpleThor(string path, string fileName, string content, bool corruptCrc = false)
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
        long dataOffset = fs.Position;
        bw.Write(compressedFile); // file data
        long tableOffset = fs.Position;
        using var tableMs = new MemoryStream();
        using (var tableBw = new BinaryWriter(tableMs, Encoding.ASCII, leaveOpen: true))
        {
            tableBw.Write((byte)fileName.Length);
            tableBw.Write(Encoding.ASCII.GetBytes(fileName));
            tableBw.Write((byte)0); // flags
            tableBw.Write((uint)0); // offset
            tableBw.Write(compressedFile.Length);
            tableBw.Write(fileData.Length);
            tableBw.Write(crc);
        }
        var tableCompressed = CompressZlib(tableMs.ToArray());
        bw.Write(tableCompressed);
        bw.Flush();
        long tableLen = tableCompressed.Length;
        fs.Position = lenPos;
        bw.Write((int)tableLen);
        fs.Position = offPos;
        bw.Write((int)tableOffset);
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

