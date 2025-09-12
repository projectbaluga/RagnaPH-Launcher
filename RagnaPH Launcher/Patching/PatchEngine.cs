using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Applies downloaded patch archives to GRF files and keeps track of state.
/// </summary>
public sealed class PatchEngine : IPatchEngine
{
    private readonly IPatchDownloader _downloader;
    private readonly PatchConfig _config;
    private readonly PatchStateStore _stateStore;
    private readonly Func<IThorReader> _thorFactory;
    private readonly Func<IGrfEditor> _grfFactory;

    public event EventHandler<PatchProgressEventArgs>? Progress;

    public PatchEngine(IPatchDownloader downloader,
                       PatchConfig config,
                       PatchStateStore stateStore,
                       Func<IThorReader> thorFactory,
                       Func<IGrfEditor> grfFactory)
    {
        _downloader = downloader;
        _config = config;
        _stateStore = stateStore;
        _thorFactory = thorFactory;
        _grfFactory = grfFactory;
    }

    public async Task ApplyPlanAsync(PatchPlan plan, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        var ordered = plan.Jobs.OrderBy(j => j.Id);

        foreach (var job in ordered)
        {
            if (state.AppliedIds.Contains(job.Id))
                continue;

            state = await ApplySingleInternalAsync(job, state, ct);
        }

        state = state with { LastAppliedId = Math.Max(state.LastAppliedId, plan.HighestRemoteId) };
        await _stateStore.SaveAsync(state, ct);
    }

    public async Task ApplySingleAsync(PatchJob job, CancellationToken ct)
    {
        var state = await _stateStore.LoadAsync(ct);
        if (!state.AppliedIds.Contains(job.Id))
        {
            state = await ApplySingleInternalAsync(job, state, ct);
            await _stateStore.SaveAsync(state, ct);
        }
    }

    private async Task<PatchState> ApplySingleInternalAsync(PatchJob job, PatchState state, CancellationToken ct)
    {
        Report("download", job.Id, null, null);
        var path = await _downloader.DownloadAsync(job, ct);

        using var thor = _thorFactory();
        var manifest = await thor.ReadManifestAsync(path, ct);

        var targetGrf = job.TargetGrf ?? manifest.TargetGrf ?? _config.Patching.DefaultTargetGrf;
        var grfPath = Path.Combine(_config.Paths.GameRoot, targetGrf);

        var merger = new GrfMerger(_grfFactory, _config.Patching);

        await merger.MergeAsync(grfPath, async grf =>
        {
            var entries = await thor.ReadEntriesAsync(path, ct);
            foreach (var entry in entries)
            {
                Report("apply", job.Id, null, null);
                switch (entry.Kind)
                {
                    case ThorEntryKind.File:
                        using (var stream = await entry.OpenStreamAsync())
                        {
                            await grf.AddOrReplaceAsync(entry.VirtualPath, stream, ct);
                        }
                        break;
                    case ThorEntryKind.Delete:
                        await grf.DeleteAsync(entry.VirtualPath, ct);
                        break;
                    case ThorEntryKind.Directory:
                        // directories are implied in GRF
                        break;
                }
            }
        }, _config.Patching.CheckIntegrity && manifest.IncludesChecksums, ct);

        state.AppliedIds.Add(job.Id);
        state = state with { LastAppliedId = Math.Max(state.LastAppliedId, job.Id) };
        return state;
    }

    private void Report(string phase, int? id, double? percent, long? bytes)
        => Progress?.Invoke(this, new PatchProgressEventArgs(phase, id, percent, bytes));
}

