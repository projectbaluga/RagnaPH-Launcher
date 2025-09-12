using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class ThorReaderTests
{
    [Fact]
    public async Task ReadManifest_InvalidFile_ThrowsFriendlyMessage()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not a zip");
            var reader = new ThorReader();
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadManifestAsync(path, CancellationToken.None));
            Assert.Contains("Invalid or corrupt THOR archive", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
