using System.Collections.Generic;

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
