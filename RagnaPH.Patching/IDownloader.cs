using System;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IDownloader
{
    Task<DownloadResult> DownloadAsync(Uri url, string destTempPath, IProgress<long> bytes, CancellationToken ct);
}

public sealed record DownloadResult(string FilePath, long Bytes);
