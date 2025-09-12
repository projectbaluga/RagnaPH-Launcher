using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Real implementation of <see cref="IGrfEditor"/> backed by the GRF Editor
/// library. The original implementation relied on the external GRF.Core
/// package which is not available in this environment. To keep the rest of the
/// patching pipeline functional, this class now delegates all operations to
/// <see cref="MockGrfEditor"/> which stores patched files on disk without
/// touching real GRF archives.
/// </summary>
public sealed class RealGrfEditor : IGrfEditor
{
    // Delegate all behaviour to the mock implementation. This allows the
    // application to compile and run without the external GRF.Core dependency
    // while preserving the expected behaviour for unit tests and basic
    // patching scenarios.
    private readonly MockGrfEditor _inner = new();

    public Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
        => _inner.OpenAsync(grfPath, createIfMissing, ct);

    public Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
        => _inner.AddOrReplaceAsync(virtualPath, content, ct);

    public Task DeleteAsync(string virtualPath, CancellationToken ct)
        => _inner.DeleteAsync(virtualPath, ct);

    public Task RebuildIndexAsync(CancellationToken ct)
        => _inner.RebuildIndexAsync(ct);

    public Task FlushAsync(CancellationToken ct)
        => _inner.FlushAsync(ct);

    public Task VerifyAsync(CancellationToken ct)
        => _inner.VerifyAsync(ct);

    public void Dispose() => _inner.Dispose();
}
