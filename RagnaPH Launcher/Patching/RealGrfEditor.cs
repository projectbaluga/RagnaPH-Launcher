using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GRF.Core;

namespace RagnaPH.Patching;

/// <summary>
/// Real implementation of <see cref="IGrfEditor"/> backed by the GRF Editor
/// library. It edits the archive in place and writes only modified entries.
/// </summary>
public sealed class RealGrfEditor : IGrfEditor
{
    private readonly GrfHolder _grf = new();

    public Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (createIfMissing && !File.Exists(grfPath))
            _grf.New();
        _grf.Open(grfPath);
        return Task.CompletedTask;
    }

    public async Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        _grf.Commands.AddFile(virtualPath, ms.ToArray(), null);
    }

    public Task DeleteAsync(string virtualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _grf.Commands.RemoveFile(virtualPath, null);
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _grf.QuickMerge();
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _grf.Save();
        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken ct)
    {
        // GRF library does not expose a verification API
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _grf.Close();
    }
}
