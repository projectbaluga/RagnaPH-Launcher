using System.IO;
using System.Text.Json;
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

        await using var stream = File.OpenRead(_path);
        var state = await JsonSerializer.DeserializeAsync<PatchState>(stream, cancellationToken: ct);
        return state ?? new PatchState(0, new());
    }

    public async Task SaveAsync(PatchState state, CancellationToken ct = default)
    {
        var tempPath = _path + ".new";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, cancellationToken: ct);
        }
        File.Move(tempPath, _path, overwrite: true);
    }
}
