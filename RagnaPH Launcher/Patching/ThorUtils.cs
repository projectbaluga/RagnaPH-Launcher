using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;

namespace RagnaPH.Patching;

public sealed class ThorIndexEntry
{
    public string VirtualPath { get; init; } = string.Empty;
    public long Offset { get; init; }
    public int CompressedSize { get; init; }
    public int UncompressedSize { get; init; }
    public bool DeleteFlag { get; init; }
}

public sealed class ThorIndex
{
    public List<ThorIndexEntry> Entries { get; } = new();
    public long DataStartOffset { get; init; }
}

public static class ThorUtils
{
    public static bool IsValidThor(string path, out ThorIndex index)
    {
        index = new ThorIndex();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        if (fs.Length < 4)
            return false;
        var magic = new byte[4];
        fs.Read(magic, 0, 4);
        if (magic[0] != (byte)'T' || magic[1] != (byte)'h' || magic[2] != (byte)'O' || magic[3] != (byte)'r')
            return false;
        index = new ThorIndex { DataStartOffset = 4 };
        try
        {
            using var archive = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                bool delete = entry.FullName.EndsWith(".delete", StringComparison.OrdinalIgnoreCase);
                var virt = delete ? entry.FullName[..^7] : entry.FullName;
                virt = virt.Replace('\\', '/');
                // attempt to open to ensure decompression works
                using var s = entry.Open();
                while (s.ReadByte() != -1) { }
                index.Entries.Add(new ThorIndexEntry
                {
                    VirtualPath = virt,
                    Offset = 0,
                    CompressedSize = (int)entry.CompressedLength,
                    UncompressedSize = (int)entry.Length,
                    DeleteFlag = delete
                });
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void ApplyThorTransactional(string thorPath, string grfPath)
    {
        var txnPath = grfPath + ".__txn";
        var bakPath = grfPath + ".bak";
        var dir = Path.GetDirectoryName(grfPath)!;
        Directory.CreateDirectory(dir);

        if (File.Exists(grfPath))
            File.Copy(grfPath, txnPath, true);
        else
            using (File.Create(txnPath)) { }

        var originalDir = Path.ChangeExtension(grfPath, ".dir");
        var txnDir = Path.ChangeExtension(txnPath, ".dir");
        if (Directory.Exists(txnDir))
            Directory.Delete(txnDir, true);
        if (Directory.Exists(originalDir))
            CopyDirectory(originalDir, txnDir);
        else
            Directory.CreateDirectory(txnDir);

        if (!IsValidThor(thorPath, out var index))
            throw new InvalidDataException("Invalid THOR archive.");

        using var archive = new ZipArchive(File.OpenRead(thorPath), ZipArchiveMode.Read);
        var editor = new MockGrfEditor();
        editor.OpenAsync(txnPath, true, CancellationToken.None).GetAwaiter().GetResult();
        foreach (var entry in index.Entries)
        {
            if (entry.DeleteFlag)
            {
                editor.DeleteAsync(entry.VirtualPath, CancellationToken.None).GetAwaiter().GetResult();
            }
            else
            {
                var zipEntry = archive.GetEntry(entry.VirtualPath) ?? archive.GetEntry(entry.VirtualPath + ".delete");
                if (zipEntry == null) continue;
                using var s = zipEntry.Open();
                editor.AddOrReplaceAsync(entry.VirtualPath, s, CancellationToken.None).GetAwaiter().GetResult();
            }
        }
        editor.FlushAsync(CancellationToken.None).GetAwaiter().GetResult();
        editor.Dispose();

        try
        {
            if (File.Exists(grfPath))
            {
                if (File.Exists(bakPath))
                    File.Delete(bakPath);
                File.Move(grfPath, bakPath);
                var originalDirBak = Path.ChangeExtension(bakPath, ".dir");
                if (Directory.Exists(originalDirBak))
                    Directory.Delete(originalDirBak, true);
                Directory.Move(originalDir, Path.ChangeExtension(bakPath, ".dir"));
            }

            File.Move(txnPath, grfPath);
            Directory.Move(txnDir, Path.ChangeExtension(grfPath, ".dir"));

            using (File.OpenRead(grfPath)) { }
            File.Delete(bakPath);
            Directory.Delete(Path.ChangeExtension(bakPath, ".dir"), true);
        }
        catch
        {
            if (File.Exists(txnPath))
                File.Delete(txnPath);
            if (Directory.Exists(txnDir))
                Directory.Delete(txnDir, true);
            if (File.Exists(bakPath))
                File.Move(bakPath, grfPath, true);
            var bakDir = Path.ChangeExtension(bakPath, ".dir");
            if (Directory.Exists(bakDir))
                Directory.Move(bakDir, originalDir);
            throw;
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var target = Path.Combine(dest, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}
