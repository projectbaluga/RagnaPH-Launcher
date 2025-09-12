using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Helper methods for downloading patch archives.
/// </summary>
internal static class PatchDownloadUtils
{
    private static readonly HttpClient _client;

    static PatchDownloadUtils()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        _client = new HttpClient(handler, disposeHandler: true);
    }

    /// <summary>
    /// Builds an absolute patch URI by encoding each segment of
    /// <paramref name="relativePath"/> separately.
    /// </summary>
    public static Uri BuildPatchUri(Uri baseUri, string relativePath)
    {
        if (baseUri == null) throw new ArgumentNullException(nameof(baseUri));
        if (relativePath == null) throw new ArgumentNullException(nameof(relativePath));

        var segments = relativePath.Trim('/')
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
        var encoded = string.Join("/", Array.ConvertAll(segments, s => Uri.EscapeDataString(s)));

        var baseStr = baseUri.ToString();
        if (!baseStr.EndsWith("/"))
            baseStr += "/";
        return new Uri(baseStr + encoded);
    }

    /// <summary>
    /// Downloads the specified URI to a temporary cache location. If the server
    /// returns 404 or 403 the method returns <c>null</c> and no file is created.
    /// </summary>
    /// <returns>The full path to the cached file or <c>null</c> if unavailable.</returns>
    public static async Task<string?> DownloadToTempAsync(Uri uri, CancellationToken ct)
    {
        // Probe with HEAD first to handle 404/403 quickly.
        using (var head = new HttpRequestMessage(HttpMethod.Head, uri))
        {
            using var headResp = await _client.SendAsync(head, ct);
            if (headResp.StatusCode == HttpStatusCode.NotFound || headResp.StatusCode == HttpStatusCode.Forbidden)
                return null;

            headResp.EnsureSuccessStatusCode();
        }

        // Determine cache path
        var cacheDir = Path.Combine(Path.GetTempPath(), "PatchCache");
        Directory.CreateDirectory(cacheDir);
        var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(uri.ToString()));
        var name = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        var tmpPath = Path.Combine(cacheDir, name + ".thor.tmp");
        var finalPath = Path.Combine(cacheDir, name + ".thor");

        long? contentLength = null;
        using (var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.Forbidden)
                return null;
            response.EnsureSuccessStatusCode();
            contentLength = response.Content.Headers.ContentLength;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fs, 81920, ct);
        }

        if (contentLength.HasValue)
        {
            var size = new FileInfo(tmpPath).Length;
            if (size != contentLength.Value)
            {
                File.Delete(tmpPath);
                throw new InvalidDataException("Downloaded file size does not match Content-Length.");
            }
        }

        if (File.Exists(finalPath))
            File.Delete(finalPath);
        File.Move(tmpPath, finalPath);
        return finalPath;
    }
}

