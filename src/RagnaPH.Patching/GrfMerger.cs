using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Handles safe merging of patch data into a GRF file.
/// When <see cref="PatchingConfig.InPlace"/> is false, a temporary
/// copy is created and swapped atomically to avoid corruption.
/// </summary>
public sealed class GrfMerger
{
    private readonly Func<IGrfEditor> _grfFactory;
    private readonly PatchingConfig _config;

    public GrfMerger(Func<IGrfEditor> grfFactory, PatchingConfig config)
    {
        _grfFactory = grfFactory;
        _config = config;
    }

    public async Task MergeAsync(string grfPath, Func<IGrfEditor, Task> apply, bool verifyIntegrity, CancellationToken ct)
    {
        if (_config.InPlace)
        {
            await using var grf = _grfFactory();
            await grf.OpenAsync(grfPath, _config.CreateGrf, ct);
            await apply(grf);
            await grf.RebuildIndexAsync(ct);
            await grf.FlushAsync(ct);
            if (verifyIntegrity)
                await grf.VerifyAsync(ct);
            return;
        }

        var directory = Path.GetDirectoryName(grfPath)!;
        Directory.CreateDirectory(directory);

        var tempPath = grfPath + ".new";
        var backupPath = grfPath + ".bak";

        if (File.Exists(tempPath))
            File.Delete(tempPath);

        if (File.Exists(grfPath))
            File.Copy(grfPath, tempPath, true);
        else if (_config.CreateGrf)
            using (File.Create(tempPath)) { }
        else
            throw new FileNotFoundException(grfPath);

        try
        {
            await using (var grf = _grfFactory())
            {
                await grf.OpenAsync(tempPath, _config.CreateGrf, ct);
                await apply(grf);
                await grf.RebuildIndexAsync(ct);
                await grf.FlushAsync(ct);
                if (verifyIntegrity)
                    await grf.VerifyAsync(ct);
            }

            if (File.Exists(grfPath))
            {
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(grfPath, backupPath, true);
            }

            File.Move(tempPath, grfPath, true);
            if (File.Exists(backupPath))
                File.Delete(backupPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            if (File.Exists(backupPath) && !File.Exists(grfPath))
                File.Move(backupPath, grfPath);
            throw;
        }
    }
}

