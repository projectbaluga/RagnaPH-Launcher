using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RagnaPH.Patching;

/// <summary>
/// Reads simplified THOR archives. This implementation expects the archive to
/// be a standard zip file with an optional <c>manifest.json</c> entry. Any other
/// file entry is treated as a patch file; entries ending with <c>.delete</c>
/// result in delete operations.
/// This is only a minimal reader to allow the rest of the patching pipeline to
/// operate in environments where a full THOR parser is not available.
/// </summary>
public sealed class ThorReader : IThorReader
{
    public Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var archive = ZipFile.OpenRead(thorPath);
        var entry = archive.GetEntry("manifest.json");
        if (entry == null)
            return Task.FromResult(new ThorManifest(null, false));
        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var manifest = JsonConvert.DeserializeObject<ManifestModel>(json);
        return Task.FromResult(new ThorManifest(manifest?.TargetGrf, manifest?.IncludesChecksums ?? false));
    }

    public Task<IEnumerable<ThorEntry>> ReadEntriesAsync(string thorPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var list = new List<ThorEntry>();
        using var archive = ZipFile.OpenRead(thorPath);
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                continue;

            if (entry.FullName.EndsWith(".delete", StringComparison.OrdinalIgnoreCase))
            {
                var target = entry.FullName.Substring(0, entry.FullName.Length - 7);
                list.Add(new ThorEntry(target, ThorEntryKind.Delete, 0, 0, null, () => Task.FromResult<Stream>(Stream.Null)));
            }
            else
            {
                list.Add(new ThorEntry(entry.FullName, ThorEntryKind.File, entry.Length, entry.CompressedLength, null, async () =>
                {
                    var ms = new MemoryStream();
                    using var source = entry.Open();
                    await source.CopyToAsync(ms, 81920, ct);
                    ms.Position = 0;
                    return ms;
                }));
            }
        }
        return Task.FromResult<IEnumerable<ThorEntry>>(list);
    }

    public void Dispose()
    {
        // nothing to dispose
    }

    private sealed class ManifestModel
    {
        public string? TargetGrf { get; set; }
        public bool IncludesChecksums { get; set; }
    }
}

