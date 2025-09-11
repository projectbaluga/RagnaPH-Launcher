using System.IO;
using System.Linq;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchStateStoreTests
{
    [Fact]
    public async Task SaveAndLoadRoundtrip()
    {
        var temp = Path.GetTempFileName();
        File.Delete(temp);
        try
        {
            var store = new PatchStateStore(temp);
            var state = await store.LoadAsync();
            Assert.Equal(0, state.LastAppliedId);

            var newState = new PatchState(5, new() { 1, 2, 3, 4, 5 });
            await store.SaveAsync(newState);

            var loaded = await store.LoadAsync();
            Assert.Equal(5, loaded.LastAppliedId);
            Assert.True(new[]{1,2,3,4,5}.All(id => loaded.AppliedIds.Contains(id)));
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}
