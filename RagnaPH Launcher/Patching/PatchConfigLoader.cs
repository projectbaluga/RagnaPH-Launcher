using System.IO;
using System.Text.Json;
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
        using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<PatchConfig>(stream, cancellationToken: ct)
                     ?? throw new InvalidDataException("Invalid patcher configuration.");

        if (config.Web.PatchServers.Count == 0)
            throw new InvalidDataException("No patch servers configured.");

        return config;
    }
}
