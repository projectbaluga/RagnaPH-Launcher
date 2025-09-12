using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Retrieves patch information from HTTP servers.
/// </summary>
public sealed class HttpPatchSource : IPatchSource
{
    private readonly HttpClient _httpClient;
    private readonly PatchConfig _config;

    public HttpPatchSource(HttpClient httpClient, PatchConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<PatchPlan> GetPlanAsync(CancellationToken ct)
    {
        foreach (var server in _config.Web.PatchServers)
        {
            try
            {
                var plist = await _httpClient.GetStringAsync(server.PlistUrl, ct);
                return PatchListParser.Parse(plist, server.PatchUrl);
            }
            catch
            {
                // try next mirror
            }
        }

        throw new HttpRequestException("All patch mirrors failed.");
    }
}
