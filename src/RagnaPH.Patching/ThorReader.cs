using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace RagnaPH.Patching;

public sealed class ThorReader : IThorReader
{
    public async Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(thorPath);
        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry == null)
            return new ThorManifest(null, false);
        await using var stream = manifestEntry.Open();
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        var root = doc.RootElement;
        var target = root.TryGetProperty("targetGrf", out var t) ? t.GetString() : null;
        var includes = root.TryGetProperty("includesChecksums", out var c) && c.GetBoolean();
        return new ThorManifest(target, includes);
    }

    public async IAsyncEnumerable<ThorEntry> ReadEntriesAsync(string thorPath, [EnumeratorCancellation] CancellationToken ct)
    {
        using var archive = ZipFile.OpenRead(thorPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName == "manifest.json")
                continue;
            var e = entry;
            yield return new ThorEntry(e.FullName.Replace("\\", "/"), ThorEntryKind.File, e.Length, e.CompressedLength, null,
                () => Task.FromResult<Stream>(e.Open()));
            await Task.Yield();
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
