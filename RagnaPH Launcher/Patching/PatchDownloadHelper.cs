using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

internal static class PatchDownloadHelper
{
    public static Uri BuildPatchUri(Uri baseUri, string relativePath)
    {
        if (baseUri is null) throw new ArgumentNullException(nameof(baseUri));
        if (relativePath is null) throw new ArgumentNullException(nameof(relativePath));

        var segments = relativePath.Split(new[] {'/', '\\'}, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
        {
            segments[i] = Uri.EscapeDataString(segments[i]);
        }
        var joined = string.Join("/", segments);
        var baseStr = baseUri.ToString();
        if (!baseStr.EndsWith("/"))
            baseStr += "/";
        return new Uri(new Uri(baseStr), joined);
    }

    public static async Task<string?> DownloadToTempAsync(HttpClient client, Uri uri, string cacheDir, CancellationToken ct)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (uri is null) throw new ArgumentNullException(nameof(uri));
        Directory.CreateDirectory(cacheDir);

        using (var headRequest = new HttpRequestMessage(HttpMethod.Head, uri))
        using (var headResponse = await client.SendAsync(headRequest, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (headResponse.StatusCode == HttpStatusCode.NotFound || headResponse.StatusCode == HttpStatusCode.Forbidden)
            {
                Logger.Log($"HEAD {uri} -> {(int)headResponse.StatusCode}");
                return null;
            }
            headResponse.EnsureSuccessStatusCode();
        }

        using var sha = SHA1.Create();
        var hash = BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(uri.ToString()))).Replace("-", string.Empty).ToLowerInvariant();
        var tmpPath = Path.Combine(cacheDir, hash + ".thor.tmp");
        var finalPath = Path.Combine(cacheDir, hash + ".thor");

        using (var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
            {
                Logger.Log($"GET {uri} -> {(int)response.StatusCode}");
                return null;
            }
            response.EnsureSuccessStatusCode();

            using (var stream = await response.Content.ReadAsStreamAsync(ct))
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fs, ct);
            }

            if (response.Content.Headers.ContentLength.HasValue)
            {
                var size = new FileInfo(tmpPath).Length;
                if (size != response.Content.Headers.ContentLength.Value)
                    throw new IOException($"Unexpected download size for {uri}");
            }
        }

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tmpPath, finalPath);
        return finalPath;
    }
}
