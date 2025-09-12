using RagnaPH.Patching;
using System.IO.Compression;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchEngineTests
{
    [Fact]
    public async Task AppliesPatchIntoZipGrf()
    {
        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(temp);
        var thorPath = Path.Combine(temp, "patch1.thor");
        using (var zip = ZipFile.Open(thorPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("hello.txt");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("hi");
        }

        var config = new PatchConfig(
            new WebConfig(new List<PatchServer>(), 30, 1, new RetryConfig(1, Array.Empty<int>())),
            new PatchingConfig("data.grf", true, false, true, 0),
            new PathConfig(temp, temp, Path.Combine(temp, "state.json"))
        );
        var downloader = new StubDownloader(thorPath);
        var stateStore = new PatchStateStore(config.Paths.AppliedIndex);
        var engine = new PatchEngine(downloader, config, stateStore, () => new ThorReader(), () => new ZipGrfEditor());
        var job = new PatchJob(1, "patch1.thor", new Uri(thorPath), null, null, null);
        var plan = new PatchPlan(1, new[] { job });
        await engine.ApplyPlanAsync(plan, CancellationToken.None);

        using var grfStream = File.OpenRead(Path.Combine(temp, "data.grf"));
        using var archive = new ZipArchive(grfStream, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("hello.txt"));
    }

    private sealed class StubDownloader(string path) : IPatchDownloader
    {
        public Task<string> DownloadAsync(PatchJob job, CancellationToken ct) => Task.FromResult(path);
    }
}
