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

        ct.ThrowIfCancellationRequested();
        string json;
        using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
        using (var reader = new StreamReader(fs))
        {
            json = await reader.ReadToEndAsync();
        }

        var state = JsonConvert.DeserializeObject<PatchState>(json);
        return state ?? new PatchState(0, new());
    }

    public async Task SaveAsync(PatchState state, CancellationToken ct = default)
    {
        var tempPath = _path + ".new";
        var json = JsonConvert.SerializeObject(state);

        ct.ThrowIfCancellationRequested();
        using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        using (var writer = new StreamWriter(fs))
        {
            await writer.WriteAsync(json);
        }
        if (File.Exists(_path))
            File.Delete(_path);
        File.Move(tempPath, _path);
    }
}
