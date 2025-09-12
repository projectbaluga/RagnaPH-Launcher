using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchStateStoreTests
{
    [Fact]
    public async Task PersistsState()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var store = new PatchStateStore(path);
        var state = new PatchState(5, new HashSet<int> {1,2,5});
        await store.SaveAsync(state);
        var loaded = await store.LoadAsync();
        Assert.Equal(state.LastAppliedId, loaded.LastAppliedId);
        Assert.Equal(state.AppliedIds, loaded.AppliedIds);
    }
}
