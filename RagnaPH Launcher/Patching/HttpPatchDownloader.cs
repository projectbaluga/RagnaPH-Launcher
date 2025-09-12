using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Launcher.Net;

namespace RagnaPH.Patching;

/// <summary>
/// Downloads patch archives over HTTP with simple retry and integrity checks.
/// </summary>
public sealed class HttpPatchDownloader : IPatchDownloader
{
    private readonly PatchConfig _config;

    public HttpPatchDownloader(HttpClient httpClient, PatchConfig config)
    {
        // The provided HttpClient is kept for backward compatibility but the
        // new implementation uses a dedicated client in
        // <see cref="PatchDownloader"/>. Having the parameter ensures DI
        // setups continue to work without modifications.
        _config = config;
    }

    public async Task<string> DownloadAsync(PatchJob job, CancellationToken ct)
    {
        var candidates = _config.Web.PatchServers
            .Select(s => PatchUrlBuilder.Build(new Uri(s.PatchUrl), job.FileName))
            .Prepend(job.DownloadUrl)
            .Select(u => u.ToString())
            .Distinct()
            .Select(u => new Uri(u))
            .ToArray();

        Exception? lastError = null;
        for (int attempt = 0; attempt < _config.Web.Retry.MaxAttempts; attempt++)
        {
            foreach (var url in candidates)
            {
                try
                {
                    var cacheDir = Path.Combine(Path.GetTempPath(), "PatchCache");
                    var path = await PatchDownloader.DownloadThorAsync(url, cacheDir, ct);

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
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    lastError = ex;
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

        if (lastError != null)
            throw lastError;

        throw new HttpRequestException($"Failed to download {job.FileName} from all servers after {_config.Web.Retry.MaxAttempts} attempts.");
    }
}

