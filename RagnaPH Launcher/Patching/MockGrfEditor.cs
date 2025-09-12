using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// A very small <see cref="IGrfEditor"/> implementation that stores files in a
/// directory next to the GRF path. This is **not** a real GRF implementation
/// but allows the patching pipeline to run without corrupting data.
/// </summary>
public sealed class MockGrfEditor : IGrfEditor
{
    private string? _rootDirectory;

    public Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var dir = Path.ChangeExtension(grfPath, ".dir");
        if (!Directory.Exists(dir))
        {
            if (createIfMissing)
            {
                Directory.CreateDirectory(dir);
            }
            else
            {
                throw new FileNotFoundException(grfPath);
            }
        }
        _rootDirectory = dir;
        return Task.CompletedTask;
    }

    public async Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();
        var path = MapPath(virtualPath);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(fs, 81920, ct);
    }

    public Task DeleteAsync(string virtualPath, CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();
        var path = MapPath(virtualPath);
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(CancellationToken ct) => Task.CompletedTask;

    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;

    public Task VerifyAsync(CancellationToken ct) => Task.CompletedTask;

    private string MapPath(string virtualPath)
    {
        var safe = virtualPath.Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(_rootDirectory!, safe);
    }

    private void EnsureOpen()
    {
        if (_rootDirectory is null)
            throw new InvalidOperationException("GRF not opened");
    }

    public void Dispose()
    {
        // nothing to dispose
    }
}

