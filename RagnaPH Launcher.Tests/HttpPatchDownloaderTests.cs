using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Launcher.Tests;

public class HttpPatchDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_InvalidThor_ThrowsInvalidData()
    {
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://localhost:54321/");
        listener.Start();

        _ = listener.GetContextAsync().ContinueWith(async ctxTask =>
        {
            var ctx = await ctxTask;
            var data = Encoding.UTF8.GetBytes("<html>not thor</html>");
            ctx.Response.ContentLength64 = data.Length;
            await ctx.Response.OutputStream.WriteAsync(data, 0, data.Length);
            ctx.Response.OutputStream.Close();
        });

        var config = new PatchConfig(
            new WebConfig(new List<PatchServer> { new("primary", "http://localhost:54321/plist.txt", "http://localhost:54321/") }, 30, 1, new RetryConfig(1, Array.Empty<int>())),
            new PatchingConfig("data.grf", false, true, true, 0),
            new PathConfig(".", "tmp", "state.json"));

        var downloader = new HttpPatchDownloader(new HttpClient(), config);
        var job = new PatchJob(1, "invalid.thor", new Uri("http://localhost:54321/invalid.thor"), null, null, null);

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(job, CancellationToken.None));

        listener.Stop();
    }
}

