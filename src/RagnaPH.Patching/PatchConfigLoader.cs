using System.Text.Json;

namespace RagnaPH.Patching;

public static class PatchConfigLoader
{
    public static async Task<PatchConfig> LoadAsync(string path, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(path);
        var config = await JsonSerializer.DeserializeAsync<PatchConfig>(stream, cancellationToken: ct)
                     ?? throw new InvalidDataException("Invalid patcher configuration.");
        if (config.Web.PatchServers.Count == 0)
            throw new InvalidDataException("No patch servers configured.");
        return config;
    }
}
