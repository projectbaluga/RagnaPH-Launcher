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
        var safeName = Path.GetFileName(job.FileName);
        var (fileName, _) = PatchNameUtils.Normalize(safeName);
        var tempDir = _config.Paths.DownloadTemp;
        Directory.CreateDirectory(tempDir);

        var candidates = _config.Web.PatchServers
            .Select(s => PatchDownloadHelper.BuildPatchUri(new Uri(s.PatchUrl), job.FileName))
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
                    var path = await PatchDownloadHelper.DownloadToTempAsync(_httpClient, url, tempDir, ct);
                    if (path == null)
                        continue;

                    if (job.SizeBytes.HasValue)
                    {
                        var size = new FileInfo(path).Length;
                        if (size != job.SizeBytes.Value)
                            throw new InvalidDataException($"Size mismatch for {job.FileName}.");
                    }

                    if (!string.IsNullOrEmpty(job.Sha256))
                    {
                        using var stream = File.OpenRead(path);
                        using var sha = SHA256.Create();
                        var hash = sha.ComputeHash(stream);
                        var hex = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
                        if (!string.Equals(hex, job.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Checksum mismatch for {job.FileName}.");
                    }

                    return path;
                }
                catch (Exception) when (!(ct.IsCancellationRequested))
                {
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

