using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public sealed class FileCacheStore : ICacheStore
{
    private readonly string _path;

    public FileCacheStore(string path)
    {
        _path = path;
    }

    public async Task<PatchCache> LoadAsync()
    {
        if (!File.Exists(_path))
            return new PatchCache(string.Empty, 0, DateTimeOffset.MinValue);
        await using var stream = File.OpenRead(_path);
        var cache = await JsonSerializer.DeserializeAsync<PatchCache>(stream);
        return cache ?? new PatchCache(string.Empty, 0, DateTimeOffset.MinValue);
    }

    public async Task SaveAsync(PatchCache cache)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        await using var stream = File.Create(_path);
        await JsonSerializer.SerializeAsync(stream, cache, new JsonSerializerOptions { WriteIndented = true });
    }
}
