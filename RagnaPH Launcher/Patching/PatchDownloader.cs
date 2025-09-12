using System;
using System.IO;
using System.Net;
using System.Net.Http;
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

            using (var req = new HttpRequestMessage(HttpMethod.Get, uri))
            using (var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    var body = resp.Content != null ? await resp.Content.ReadAsStringAsync() : string.Empty;
                    if (body.Length > 512)
                        body = body.Substring(0, 512);
                    // Show encoded URL so we can verify it visually
                    throw new HttpRequestException($"Download failed ({(int)resp.StatusCode} {resp.StatusCode}): {uri.AbsoluteUri}\n{body}");
                }

                Directory.CreateDirectory(cacheDir);
                // Use the file name from the URL if possible so that errors refer to
                // a meaningful patch name instead of a random one. Fall back to a
                // generated name when the URL does not end with a file.
                var fileName = Path.GetFileName(uri.LocalPath);
                if (string.IsNullOrEmpty(fileName))
                    fileName = Guid.NewGuid().ToString("n") + ".thor";

                var tmp = Path.Combine(cacheDir, fileName + ".tmp");
                var fin = Path.Combine(cacheDir, fileName);

                using (var src = await resp.Content.ReadAsStreamAsync())
                using (var dst = File.Create(tmp, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan))
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
}
