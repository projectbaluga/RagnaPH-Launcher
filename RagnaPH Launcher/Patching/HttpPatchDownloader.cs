using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Downloads patch archives over HTTP with simple retry and integrity checks.
/// </summary>
public sealed class HttpPatchDownloader : IPatchDownloader
{
    private readonly HttpClient _httpClient;
    private readonly PatchConfig _config;

    public HttpPatchDownloader(HttpClient httpClient, PatchConfig config)
    {
        _httpClient = httpClient;
        _config = config;
    }

    public async Task<string> DownloadAsync(PatchJob job, CancellationToken ct)
    {
        var (fileName, encodedName) = PatchNameUtils.Normalize(job.FileName);

        var tempDir = _config.Paths.DownloadTemp;
        Directory.CreateDirectory(tempDir);
        var destPath = Path.Combine(tempDir, fileName);

        var candidates = _config.Web.PatchServers
            .Select(s =>
            {
                var baseUri = s.PatchUrl.EndsWith("/") ? s.PatchUrl : s.PatchUrl + "/";
                return new Uri(new Uri(baseUri), encodedName);
            })
            .Prepend(job.DownloadUrl)
            .Select(u => u.ToString())
            .Distinct()
            .Select(u => new Uri(u))
            .ToArray();

        for (int attempt = 0; attempt < _config.Web.Retry.MaxAttempts; attempt++)
        {
            foreach (var url in candidates)
            {
                try
                {
                    using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();

                    using (var fs = File.Create(destPath))
                    {
                        await response.Content.CopyToAsync(fs);
                    }

                    if (job.SizeBytes.HasValue)
                    {
                        var size = new FileInfo(destPath).Length;
                        if (size != job.SizeBytes.Value)
                            throw new InvalidDataException($"Size mismatch for {job.FileName}.");
                    }

                    if (!string.IsNullOrEmpty(job.Sha256))
                    {
                        using var stream = File.OpenRead(destPath);
                        using var sha = SHA256.Create();
                        var hash = sha.ComputeHash(stream);
                        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                        if (!string.Equals(hex, job.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Checksum mismatch for {job.FileName}.");
                    }

                    return destPath;
                }
                catch (Exception) when (!(ct.IsCancellationRequested))
                {
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    // try next mirror
                }
            }

            if (attempt + 1 < _config.Web.Retry.MaxAttempts)
            {
                var delay = attempt < _config.Web.Retry.BackoffSeconds.Length
                    ? _config.Web.Retry.BackoffSeconds[attempt]
                    : _config.Web.Retry.BackoffSeconds.LastOrDefault();
                if (delay > 0)
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }

        throw new HttpRequestException($"Failed to download {job.FileName} from all servers after {_config.Web.Retry.MaxAttempts} attempts.");
    }
}

