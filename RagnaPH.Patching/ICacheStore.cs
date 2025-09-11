using System;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface ICacheStore
{
    Task<PatchCache> LoadAsync();
    Task SaveAsync(PatchCache cache);
}

public sealed record PatchCache(string Server, int LastIndex, DateTimeOffset UpdatedAt);
