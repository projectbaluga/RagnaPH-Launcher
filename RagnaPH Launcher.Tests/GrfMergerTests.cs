using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class GrfMergerTests
{
    [Fact]
    public async Task SkipBackup_DoesNotCreateBakFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(tempDir);
        try
        {
            var grfPath = Path.Combine(tempDir, "data.grf");
            await File.WriteAllTextAsync(grfPath, "original");

            var config = new PatchingConfig(grfPath, InPlace: true, CheckIntegrity: false, CreateGrf: true, SkipBackup: true, EnforceFreeSpaceMB: 0);
            var merger = new GrfMerger(() => new MockGrfEditor(), config);

            await merger.MergeAsync(grfPath, _ => Task.CompletedTask, verifyIntegrity: false, ct: default);

            Assert.False(File.Exists(grfPath + ".bak"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
