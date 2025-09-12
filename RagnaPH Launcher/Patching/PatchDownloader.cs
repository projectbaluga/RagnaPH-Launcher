using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Launcher.Net;

namespace RagnaPH.Patching {
    /// <summary>
    /// Downloads patch archives to a local cache and exposes helpful error
    /// messages that include the final resolved URL.
    /// </summary>
    internal static class PatchDownloader {
        private static readonly HttpClient _http = new HttpClient(new HttpClientHandler {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        });

        public static Task<string> DownloadThorAsync(Uri baseUri, string manifestFilePath, string cacheDir, CancellationToken ct)
            => DownloadThorAsync(PatchUrlBuilder.Build(baseUri, manifestFilePath), cacheDir, ct);

        public static async Task<string> DownloadThorAsync(Uri uri, string cacheDir, CancellationToken ct) {
            if (uri == null) throw new ArgumentNullException(nameof(uri));
            if (cacheDir == null) throw new ArgumentNullException(nameof(cacheDir));

            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode) {
                var body = resp.Content != null ? await resp.Content.ReadAsStringAsync(ct) : string.Empty;
                if (body.Length > 512) body = body[..512];
                // Show encoded URL so we can verify it visually
                throw new HttpRequestException($"Download failed ({(int)resp.StatusCode} {resp.StatusCode}): {uri.AbsoluteUri}\n{body}");
            }

            Directory.CreateDirectory(cacheDir);
            var tmp = Path.Combine(cacheDir, Path.GetRandomFileName() + ".thor.tmp");
            var fin = Path.ChangeExtension(tmp, ".thor");

            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = File.Create(tmp, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan)) {
                await src.CopyToAsync(dst, 81920, ct);
            }
            File.Move(tmp, fin, true);
            return fin;
        }
    }
}
