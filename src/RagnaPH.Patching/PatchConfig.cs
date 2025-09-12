namespace RagnaPH.Patching;

public record PatchConfig(WebConfig Web, PatchingConfig Patching, PathConfig Paths);

public record WebConfig(List<PatchServer> PatchServers, int TimeoutSeconds, int MaxParallelDownloads, RetryConfig Retry);

public record PatchServer(string Name, string PlistUrl, string PatchUrl);

public record RetryConfig(int MaxAttempts, int[] BackoffSeconds);

public record PatchingConfig(string DefaultTargetGrf, bool InPlace, bool CheckIntegrity, bool CreateGrf, int EnforceFreeSpaceMB);

public record PathConfig(string GameRoot, string DownloadTemp, string AppliedIndex);

public record PatchPlan(int HighestRemoteId, IReadOnlyList<PatchJob> Jobs);

public record PatchJob(int Id, string FileName, Uri DownloadUrl, string? TargetGrf, long? SizeBytes, string? Sha256);

public record PatchState(int LastAppliedId, HashSet<int> AppliedIds);

public record ThorManifest(string? TargetGrf, bool IncludesChecksums);

public record ThorEntry(string VirtualPath, ThorEntryKind Kind, long UncompressedSize, long CompressedSize, string? Sha256, Func<Task<Stream>> OpenStreamAsync);

public enum ThorEntryKind { File, Delete, Directory }

public record PatchProgressEventArgs(string Phase, int? CurrentId, double? Percent, long? BytesDownloaded);
