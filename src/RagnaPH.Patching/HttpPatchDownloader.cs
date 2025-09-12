using System.Net.Http;
using System.Security.Cryptography;

namespace RagnaPH.Patching;

public sealed class HttpPatchDownloader(HttpClient httpClient, PatchConfig config) : IPatchDownloader
{
    public async Task<string> DownloadAsync(PatchJob job, CancellationToken ct)
    {
        var fileName = Path.GetFileName(job.FileName);
        if (!string.Equals(fileName, job.FileName, StringComparison.Ordinal))
            throw new InvalidDataException("Invalid patch file name.");

        var tempDir = config.Paths.DownloadTemp;
        Directory.CreateDirectory(tempDir);
        var destPath = Path.Combine(tempDir, fileName);

        var encodedName = Uri.EscapeDataString(fileName);
        var candidates = config.Web.PatchServers
            .Select(s => new Uri((s.PatchUrl.EndsWith("/") ? s.PatchUrl : s.PatchUrl + "/") + encodedName))
            .Prepend(job.DownloadUrl)
            .Select(u => u.ToString())
            .Distinct()
            .Select(u => new Uri(u))
            .ToArray();

        for (int attempt = 0; attempt < config.Web.Retry.MaxAttempts; attempt++)
        {
            foreach (var url in candidates)
            {
                try
                {
                    await using var stream = await httpClient.GetStreamAsync(url, ct);
                    await using (var fs = File.Create(destPath))
                    {
                        await stream.CopyToAsync(fs, ct);
                    }

                    if (job.SizeBytes.HasValue)
                    {
                        var size = new FileInfo(destPath).Length;
                        if (size != job.SizeBytes.Value)
                            throw new InvalidDataException($"Size mismatch for {job.FileName}.");
                    }

                    if (!string.IsNullOrEmpty(job.Sha256))
                    {
                        await using var file = File.OpenRead(destPath);
                        var hash = await SHA256.HashDataAsync(file, ct);
                        var hex = Convert.ToHexString(hash).ToLowerInvariant();
                        if (!string.Equals(hex, job.Sha256, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Checksum mismatch for {job.FileName}.");
                    }

                    return destPath;
                }
                catch when (!ct.IsCancellationRequested)
                {
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                }
            }

            if (attempt + 1 < config.Web.Retry.MaxAttempts)
            {
                var delay = attempt < config.Web.Retry.BackoffSeconds.Length
                    ? config.Web.Retry.BackoffSeconds[attempt]
                    : config.Web.Retry.BackoffSeconds.LastOrDefault();
                if (delay > 0)
                    await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }

        throw new HttpRequestException($"Failed to download {job.FileName}.");
    }
}
