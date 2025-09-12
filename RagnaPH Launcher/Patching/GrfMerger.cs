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
        ct.ThrowIfCancellationRequested();

        if (_config.EnforceFreeSpaceMB > 0)
        {
            var root = Path.GetPathRoot(Path.GetFullPath(grfPath))!;
            var drive = new DriveInfo(root);
            var requiredBytes = _config.EnforceFreeSpaceMB * 1024L * 1024L;
            if (drive.AvailableFreeSpace < requiredBytes)
                throw new IOException($"Not enough free space on drive {drive.Name}. Required: {_config.EnforceFreeSpaceMB} MB");
        }

        if (_config.InPlace)
        {
            var inPlaceBackupPath = grfPath + ".bak";

            if (!_config.SkipBackup)
            {
                if (File.Exists(inPlaceBackupPath))
                    File.Delete(inPlaceBackupPath);

                if (File.Exists(grfPath))
                    File.Copy(grfPath, inPlaceBackupPath, true);
                else if (_config.CreateGrf)
                    using (File.Create(grfPath)) { }
                else
                    throw new FileNotFoundException(grfPath);
            }
            else if (!File.Exists(grfPath))
            {
                if (_config.CreateGrf)
                    using (File.Create(grfPath)) { }
                else
                    throw new FileNotFoundException(grfPath);
            }

            try
            {
                using var grf = _grfFactory();
                await grf.OpenAsync(grfPath, _config.CreateGrf, ct);
                await apply(grf);
                await grf.RebuildIndexAsync(ct);
                await grf.FlushAsync(ct);
                if (verifyIntegrity)
                    await grf.VerifyAsync(ct);

                if (!_config.SkipBackup && File.Exists(inPlaceBackupPath))
                    File.Delete(inPlaceBackupPath);
            }
            catch
            {
                if (!_config.SkipBackup && File.Exists(inPlaceBackupPath))
                {
                    File.Copy(inPlaceBackupPath, grfPath, true);
                    File.Delete(inPlaceBackupPath);
                }
                throw;
            }

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
            using (var grf = _grfFactory())
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
                if (_config.SkipBackup)
                {
                    File.Delete(grfPath);
                }
                else
                {
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);
                    File.Move(grfPath, backupPath);
                }
            }

            File.Move(tempPath, grfPath);
            if (!_config.SkipBackup && File.Exists(backupPath))
                File.Delete(backupPath);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            if (!_config.SkipBackup && File.Exists(backupPath) && !File.Exists(grfPath))
                File.Move(backupPath, grfPath);
            throw;
        }
    }
}

