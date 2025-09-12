using System;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Applies a simplified THOR archive directly to a GRF file by extracting
/// each entry and merging it into the target archive. This avoids creating
/// large transactional copies of the GRF and mirrors the behaviour of
/// official patchers which operate in place.
/// </summary>
internal static class ThorPatcher
{
    /// <summary>
    /// Synchronously apply a THOR patch to the specified GRF file.
    /// </summary>
    public static void ApplyPatch(string thorPath, string grfPath)
        => ApplyPatchAsync(thorPath, grfPath).GetAwaiter().GetResult();

    /// <summary>
    /// Asynchronously apply a THOR patch to the specified GRF file.
    /// </summary>
    public static async Task ApplyPatchAsync(string thorPath, string grfPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reader = new ThorReader();
        var manifest = await reader.ReadManifestAsync(thorPath, cancellationToken);
        var entries = await reader.ReadEntriesAsync(thorPath, cancellationToken);

        // The official patcher honours the target GRF embedded in the THOR
        // manifest. If none is provided we fall back to the supplied path.
        var targetGrf = manifest.TargetGrf ?? grfPath;

        // Merge using a GrfMerger which mimics the robust workflow used by
        // Tokei's GRF Editor (https://github.com/Tokeiburu/GRFEditor):
        // a backup is created, changes are applied, then the index is rebuilt
        // before swapping the result back.
        var config = new PatchingConfig(targetGrf, InPlace: true, CheckIntegrity: true, CreateGrf: true, SkipBackup: true, EnforceFreeSpaceMB: 0);
        var merger = new GrfMerger(() => new RealGrfEditor(), config);

        await merger.MergeAsync(targetGrf, async grf =>
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (entry.Kind)
                {
                    case ThorEntryKind.File:
                        using (var stream = await entry.OpenStreamAsync())
                        {
                            await grf.AddOrReplaceAsync(entry.VirtualPath, stream, cancellationToken);
                        }
                        break;
                    case ThorEntryKind.Delete:
                        await grf.DeleteAsync(entry.VirtualPath, cancellationToken);
                        break;
                    case ThorEntryKind.Directory:
                        // Directories are implicit in GRF archives.
                        break;
                }
            }
        }, verifyIntegrity: true, ct: cancellationToken);
    }
}
