using System.IO;
using Newtonsoft.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Loads <see cref="PatchConfig"/> from a JSON file with basic validation.
/// </summary>
public static class PatchConfigLoader
{
    public static async Task<PatchConfig> LoadAsync(string path, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(path, ct);
        var config = JsonConvert.DeserializeObject<PatchConfig>(json)
                     ?? throw new InvalidDataException("Invalid patcher configuration.");

        if (config.Web.PatchServers.Count == 0)
            throw new InvalidDataException("No patch servers configured.");

        return config;
    }
}
