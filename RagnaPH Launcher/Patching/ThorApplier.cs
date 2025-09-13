using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Applies the contents of a <see cref="ThorArchive"/> to a target GRF
/// archive. The application is performed through <see cref="GrfMerger"/>
/// which ensures that the file table is rebuilt safely and atomically.
/// </summary>
public sealed class ThorApplier
{
    private readonly Func<IGrfEditor> _grfFactory;
    private readonly PatchingConfig _config;

    public ThorApplier(Func<IGrfEditor> grfFactory, PatchingConfig config)
    {
        _grfFactory = grfFactory;
        _config = config;
    }

    /// <summary>
    /// Applies the THOR archive to the specified GRF file path. The caller is
    /// responsible for resolving the final path, typically by combining the
    /// game root with the desired target GRF name.
    /// </summary>
    public async Task ApplyAsync(ThorArchive archive, string grfPath, CancellationToken ct = default)
    {
        var merger = new GrfMerger(_grfFactory, _config);

        await merger.MergeAsync(grfPath, async grf =>
        {
            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                switch (entry.Kind)
                {
                    case ThorArchive.ThorEntryKind.File:
                        using (var stream = await archive.OpenEntryStreamAsync(entry))
                        {
                            await grf.AddOrReplaceAsync(entry.VirtualPath, stream, ct);
                        }
                        break;
                    case ThorArchive.ThorEntryKind.Delete:
                        await grf.DeleteAsync(entry.VirtualPath, ct);
                        break;
                    case ThorArchive.ThorEntryKind.Directory:
                        // Directories are implicit in GRF archives.
                        break;
                }
            }
        }, verifyIntegrity: _config.CheckIntegrity, ct: ct);
    }
}

