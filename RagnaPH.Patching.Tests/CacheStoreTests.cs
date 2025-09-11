using System;
using System.IO;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class CacheStoreTests
{
    [Fact]
    public async Task SavesAndLoads()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var store = new FileCacheStore(path);
        var cache = new PatchCache("main", 2, DateTimeOffset.UtcNow);
        await store.SaveAsync(cache);
        var loaded = await store.LoadAsync();
        Assert.Equal(cache.Server, loaded.Server);
        Assert.Equal(cache.LastIndex, loaded.LastIndex);
    }
}
