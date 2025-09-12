namespace RagnaPH.Patching;

public sealed class PatchEngine(IPatchDownloader downloader,
                                PatchConfig config,
                                PatchStateStore stateStore,
                                Func<IThorReader> thorFactory,
                                Func<IGrfEditor> grfFactory) : IPatchEngine
{
    public event EventHandler<PatchProgressEventArgs>? Progress;

    public async Task ApplyPlanAsync(PatchPlan plan, CancellationToken ct)
    {
        var state = await stateStore.LoadAsync(ct);
        var ordered = plan.Jobs.OrderBy(j => j.Id);
        foreach (var job in ordered)
        {
            if (state.AppliedIds.Contains(job.Id))
                continue;
            state = await ApplySingleInternalAsync(job, state, ct);
        }
        state = state with { LastAppliedId = Math.Max(state.LastAppliedId, plan.HighestRemoteId) };
        await stateStore.SaveAsync(state, ct);
    }

    public async Task ApplySingleAsync(PatchJob job, CancellationToken ct)
    {
        var state = await stateStore.LoadAsync(ct);
        if (!state.AppliedIds.Contains(job.Id))
        {
            state = await ApplySingleInternalAsync(job, state, ct);
            await stateStore.SaveAsync(state, ct);
        }
    }

    private async Task<PatchState> ApplySingleInternalAsync(PatchJob job, PatchState state, CancellationToken ct)
    {
        Report("download", job.Id, null, null);
        var path = await downloader.DownloadAsync(job, ct);

        await using var thor = thorFactory();
        var manifest = await thor.ReadManifestAsync(path, ct);
        var targetGrf = job.TargetGrf ?? manifest.TargetGrf ?? config.Patching.DefaultTargetGrf;
        var grfPath = Path.Combine(config.Paths.GameRoot, targetGrf);
        var merger = new GrfMerger(grfFactory, config.Patching);
        await merger.MergeAsync(grfPath, async grf =>
        {
            await foreach (var entry in thor.ReadEntriesAsync(path, ct))
            {
                Report("apply", job.Id, null, null);
                switch (entry.Kind)
                {
                    case ThorEntryKind.File:
                        await using (var stream = await entry.OpenStreamAsync())
                        {
                            await grf.AddOrReplaceAsync(entry.VirtualPath, stream, ct);
                        }
                        break;
                    case ThorEntryKind.Delete:
                        await grf.DeleteAsync(entry.VirtualPath, ct);
                        break;
                }
            }
        }, config.Patching.CheckIntegrity && manifest.IncludesChecksums, ct);
        state.AppliedIds.Add(job.Id);
        state = state with { LastAppliedId = Math.Max(state.LastAppliedId, job.Id) };
        return state;
    }

    private void Report(string phase, int? id, double? percent, long? bytes) =>
        Progress?.Invoke(this, new PatchProgressEventArgs(phase, id, percent, bytes));
}
