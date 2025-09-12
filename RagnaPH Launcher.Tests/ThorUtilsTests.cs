using System;
using System.IO;
using System.IO.Compression;
using Xunit;
using RagnaPH.Patching;

public class ThorUtilsTests
{
    [Fact]
    public void BuildPatchUri_EncodesSegments()
    {
        var baseUri = new Uri("http://example.com/patches/");
        var uri = PatchDownloadHelper.BuildPatchUri(baseUri, "dir with spaces/file#1.thor");
        Assert.Equal("http://example.com/patches/dir%20with%20spaces/file%231.thor", uri.ToString());
    }

    [Fact]
    public void IsValidThor_ReadsEntries()
    {
        var temp = Path.GetTempFileName();
        File.Delete(temp);
        using (var ms = new MemoryStream())
        {
            ms.Write(new byte[] { (byte)'T', (byte)'h', (byte)'O', (byte)'r' });
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var e = archive.CreateEntry("data/test.txt");
                using var s = new StreamWriter(e.Open());
                s.Write("hello");
                archive.CreateEntry("data/old.delete");
            }
            File.WriteAllBytes(temp, ms.ToArray());
        }
        Assert.True(ThorUtils.IsValidThor(temp, out var index));
        Assert.Equal(2, index.Entries.Count);
        Assert.Contains(index.Entries, e => e.VirtualPath == "data/test.txt" && !e.DeleteFlag);
        Assert.Contains(index.Entries, e => e.VirtualPath == "data/old" && e.DeleteFlag);
        File.Delete(temp);
    }

    [Fact]
    public void ApplyThorTransactional_AddsAndDeletes()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var grf = Path.Combine(root, "data.grf");
        File.WriteAllBytes(grf, Array.Empty<byte>());
        var grfDir = Path.ChangeExtension(grf, ".dir");
        Directory.CreateDirectory(Path.Combine(grfDir, "data"));
        File.WriteAllText(Path.Combine(grfDir, "data", "old.txt"), "old");

        var thor = Path.Combine(root, "patch.thor");
        using (var ms = new MemoryStream())
        {
            ms.Write(new byte[] { (byte)'T', (byte)'h', (byte)'O', (byte)'r' });
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var e = archive.CreateEntry("data/new.txt");
                using var sw = new StreamWriter(e.Open());
                sw.Write("hello");
                archive.CreateEntry("data/old.txt.delete");
            }
            File.WriteAllBytes(thor, ms.ToArray());
        }

        ThorUtils.ApplyThorTransactional(thor, grf);

        Assert.True(File.Exists(Path.Combine(grfDir, "data", "new.txt")));
        Assert.False(File.Exists(Path.Combine(grfDir, "data", "old.txt")));

        Directory.Delete(root, true);
    }
}
