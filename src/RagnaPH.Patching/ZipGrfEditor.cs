using System.IO.Compression;

namespace RagnaPH.Patching;

public sealed class ZipGrfEditor : IGrfEditor
{
    private FileStream? _stream;
    private ZipArchive? _archive;

    public Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
    {
        if (!File.Exists(grfPath))
        {
            if (!createIfMissing) throw new FileNotFoundException(grfPath);
            using (File.Create(grfPath)) { }
        }
        _stream = new FileStream(grfPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        _archive = new ZipArchive(_stream, ZipArchiveMode.Update, leaveOpen: true);
        return Task.CompletedTask;
    }

    public async Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
    {
        var entry = _archive!.GetEntry(virtualPath);
        entry?.Delete();
        entry = _archive.CreateEntry(virtualPath, CompressionLevel.Optimal);
        await using var stream = entry.Open();
        await content.CopyToAsync(stream, ct);
    }

    public Task DeleteAsync(string virtualPath, CancellationToken ct)
    {
        var entry = _archive!.GetEntry(virtualPath);
        entry?.Delete();
        return Task.CompletedTask;
    }

    public Task RebuildIndexAsync(CancellationToken ct) => Task.CompletedTask;
    public Task FlushAsync(CancellationToken ct)
    {
        _archive?.Dispose();
        _archive = new ZipArchive(_stream!, ZipArchiveMode.Update, leaveOpen: true);
        return Task.CompletedTask;
    }
    public Task VerifyAsync(CancellationToken ct) => Task.CompletedTask;

    public ValueTask DisposeAsync()
    {
        _archive?.Dispose();
        _stream?.Dispose();
        return ValueTask.CompletedTask;
    }
}
