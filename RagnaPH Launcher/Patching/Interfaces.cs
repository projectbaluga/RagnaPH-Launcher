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

public interface IThorReader : IDisposable
{
    Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct);
    Task<IEnumerable<ThorEntry>> ReadEntriesAsync(string thorPath, CancellationToken ct);
}

public record ThorManifest(string? TargetGrf, bool IncludesChecksums);

public record ThorEntry(string VirtualPath, ThorEntryKind Kind, long UncompressedSize, long CompressedSize, string? Sha256, Func<Task<Stream>> OpenStreamAsync);

public enum ThorEntryKind { File, Delete, Directory }

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
