using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchListProviderTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly string _content;
        public FakeHandler(string content) => _content = content;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_content) });
    }

    [Fact]
    public async Task ParsesValidList()
    {
        var plist = "1 base.thor\n2 ui.thor\n";
        var client = new HttpClient(new FakeHandler(plist));
        var provider = new PatchListProvider(client);
        var list = await provider.FetchAsync(new Uri("https://example.com/patch/plist.txt"), CancellationToken.None);
        Assert.Equal(2, list.Count);
        Assert.Equal(1, list[0].Index);
        Assert.Equal("base.thor", list[0].FileName);
        Assert.Equal("https://example.com/patch/base.thor", list[0].RemoteUrl.ToString());
    }

    [Fact]
    public async Task ThrowsOnNonIncreasing()
    {
        var plist = "2 a.thor\n1 b.thor\n";
        var client = new HttpClient(new FakeHandler(plist));
        var provider = new PatchListProvider(client);
        await Assert.ThrowsAsync<FormatException>(async () =>
            await provider.FetchAsync(new Uri("https://x/"), CancellationToken.None));
    }
}
