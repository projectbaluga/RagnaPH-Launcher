using System.Net.Http;

namespace RagnaPH.Patching;

public sealed class HttpPatchSource(HttpClient httpClient, PatchConfig config) : IPatchSource
{
    public async Task<PatchPlan> GetPlanAsync(CancellationToken ct)
    {
        foreach (var server in config.Web.PatchServers)
        {
            try
            {
                var plist = await httpClient.GetStringAsync(server.PlistUrl, ct);
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
