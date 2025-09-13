using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IPatchSource
{
    Task<PatchPlan> GetPlanAsync(CancellationToken ct);
}

public interface IPatchDownloader
{
    Task<string> DownloadAsync(PatchJob job, CancellationToken ct);
}

public interface IGrfEditor : IDisposable
{
    Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct);
    Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct);
    Task DeleteAsync(string virtualPath, CancellationToken ct);
    Task RebuildIndexAsync(CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
    Task VerifyAsync(CancellationToken ct);
}

public interface IPatchEngine
{
    event EventHandler<PatchProgressEventArgs> Progress;
    Task ApplyPlanAsync(PatchPlan plan, CancellationToken ct);
    Task ApplySingleAsync(PatchJob job, CancellationToken ct);
}

public record PatchProgressEventArgs(string Phase, int? CurrentId, double? Percent, long? BytesDownloaded);
