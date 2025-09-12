namespace RagnaPH.Patching;

public interface IPatchSource
{
    Task<PatchPlan> GetPlanAsync(CancellationToken ct);
}

public interface IPatchDownloader
{
    Task<string> DownloadAsync(PatchJob job, CancellationToken ct);
}

public interface IThorReader : IAsyncDisposable
{
    Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct);
    IAsyncEnumerable<ThorEntry> ReadEntriesAsync(string thorPath, CancellationToken ct);
}

public interface IGrfEditor : IAsyncDisposable
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
