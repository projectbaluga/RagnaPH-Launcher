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
    private readonly Func<IGrfEditor> _grfFactory;

    public event EventHandler<PatchProgressEventArgs>? Progress;

    public PatchEngine(IPatchDownloader downloader,
                       PatchConfig config,
                       PatchStateStore stateStore,
                       Func<IGrfEditor> grfFactory)
    {
        _downloader = downloader;
        _config = config;
        _stateStore = stateStore;
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

        using var archive = ThorArchive.Open(path);
        var targetName = job.TargetGrf ?? archive.TargetGrf ?? _config.Patching.DefaultTargetGrf;
        var grfPath = Path.Combine(_config.Paths.GameRoot, targetName);

        var applier = new ThorApplier(_grfFactory, _config.Patching);
        Report("apply", job.Id, null, null);
        await applier.ApplyAsync(archive, grfPath, ct);

        state.AppliedIds.Add(job.Id);
        state = state with { LastAppliedId = Math.Max(state.LastAppliedId, job.Id) };
        return state;
    }

    private void Report(string phase, int? id, double? percent, long? bytes)
        => Progress?.Invoke(this, new PatchProgressEventArgs(phase, id, percent, bytes));
}

