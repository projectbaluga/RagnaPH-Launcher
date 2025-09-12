using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Launcher.Net;

namespace RagnaPH.Patching
{
    /// <summary>
    /// Downloads patch archives to a local cache and exposes helpful error
    /// messages that include the final resolved URL.
    /// </summary>
    internal static class PatchDownloader
    {
        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        public static Task<string> DownloadThorAsync(Uri baseUri, string manifestFilePath, string cacheDir, CancellationToken ct)
            => DownloadThorAsync(PatchUrlBuilder.Build(baseUri, manifestFilePath), cacheDir, ct);

        public static async Task<string> DownloadThorAsync(Uri uri, string cacheDir, CancellationToken ct)
        {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (cacheDir == null) throw new ArgumentNullException(nameof(cacheDir));

            using (var head = new HttpRequestMessage(HttpMethod.Head, uri))
            using (var headResp = await _http.SendAsync(head, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (headResp.StatusCode == HttpStatusCode.NotFound)
                    throw new HttpRequestException($"Download failed (404 NotFound): {uri}");
            }

            Directory.CreateDirectory(cacheDir);
            byte[] hash;
            using (var sha = SHA1.Create())
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(uri.ToString()));
            var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
            var tmp = Path.Combine(cacheDir, $"{hex}.thor.tmp");
            var fin = Path.ChangeExtension(tmp, ".thor");

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
            var body = resp.Content != null ? await resp.Content.ReadAsStringAsync() : string.Empty;
            if (body.Length > 512)
                body = body.Substring(0, 512);
            throw new HttpRequestException($"Download failed ({(int)resp.StatusCode} {resp.StatusCode}): {uri}\n{body}");
        }

        using (var src = await resp.Content.ReadAsStreamAsync())
        using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await src.CopyToAsync(dst, 81920, ct);
        }

        if (File.Exists(fin))
            File.Delete(fin);
        File.Move(tmp, fin);
        return fin;
    }
}
}

