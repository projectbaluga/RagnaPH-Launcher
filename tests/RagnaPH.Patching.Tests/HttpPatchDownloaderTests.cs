using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class HttpPatchDownloaderTests
{
    [Fact]
    public async Task DownloadsFileAndVerifiesChecksum()
    {
        var content = Encoding.UTF8.GetBytes("hello world");
        var sha = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var handler = new StaticHandler(content);
        var client = new HttpClient(handler);

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PatchConfig(
                new WebConfig(new(), 30, 1, new RetryConfig(1, Array.Empty<int>())),
                new PatchingConfig("data.grf", false, true, true, 512),
                new PathConfig(".", tempDir, "state.json"));

            var downloader = new HttpPatchDownloader(client, config);
            var job = new PatchJob(1, "patch1.thor", new Uri("http://example/patch1.thor"), null, content.Length, sha);

            var path = await downloader.DownloadAsync(job, CancellationToken.None);

            Assert.True(File.Exists(path));
            var downloaded = await File.ReadAllBytesAsync(path);
            Assert.Equal(content, downloaded);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task RetriesOnFailure()
    {
        var content = new byte[] {1,2,3};
        var handler = new FlakyHandler(content);
        var client = new HttpClient(handler);
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var config = new PatchConfig(
                new WebConfig(new(), 30, 1, new RetryConfig(3, new[]{0,0,0})),
                new PatchingConfig("data.grf", false, true, true, 512),
                new PathConfig(".", tempDir, "state.json"));

            var downloader = new HttpPatchDownloader(client, config);
            var job = new PatchJob(1, "patch.thor", new Uri("http://example/patch.thor"), null, null, null);

            var path = await downloader.DownloadAsync(job, CancellationToken.None);
            Assert.True(File.Exists(path));
            Assert.Equal(2, handler.Attempts);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        public StaticHandler(byte[] content) => _content = content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FlakyHandler : HttpMessageHandler
    {
        private readonly byte[] _content;
        public int Attempts { get; private set; }
        public FlakyHandler(byte[] content) => _content = content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Attempts++;
            if (Attempts == 1)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_content)
            };
            return Task.FromResult(response);
        }
    }
}

