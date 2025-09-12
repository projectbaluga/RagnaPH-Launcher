using System.IO;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Persists and loads patch application state.
/// </summary>
public sealed class PatchStateStore
{
    private readonly string _path;

    public PatchStateStore(string path)
    {
        _path = path;
    }

    public async Task<PatchState> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return new PatchState(0, new());

        var json = await File.ReadAllTextAsync(_path, ct);
        var state = JsonConvert.DeserializeObject<PatchState>(json);
        return state ?? new PatchState(0, new());
    }

    public async Task SaveAsync(PatchState state, CancellationToken ct = default)
    {
        var tempPath = _path + ".new";
        var json = JsonConvert.SerializeObject(state);
        await File.WriteAllTextAsync(tempPath, json, ct);
        if (File.Exists(_path))
            File.Delete(_path);
        File.Move(tempPath, _path);
    }
}
