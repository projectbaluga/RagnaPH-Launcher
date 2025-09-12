namespace RagnaPH.Patching;

public sealed class GrfMerger(Func<IGrfEditor> grfFactory, PatchingConfig config)
{
    public async Task MergeAsync(string grfPath, Func<IGrfEditor, Task> apply, bool verifyIntegrity, CancellationToken ct)
    {
        if (config.InPlace)
        {
            await using var grf = grfFactory();
            await grf.OpenAsync(grfPath, config.CreateGrf, ct);
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
        else if (config.CreateGrf)
            using (File.Create(tempPath)) { }
        else
            throw new FileNotFoundException(grfPath);

        try
        {
            await using (var grf = grfFactory())
            {
                await grf.OpenAsync(tempPath, config.CreateGrf, ct);
                await apply(grf);
                await grf.RebuildIndexAsync(ct);
                await grf.FlushAsync(ct);
                if (verifyIntegrity)
                    await grf.VerifyAsync(ct);
            }
            if (File.Exists(grfPath))
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                File.Move(grfPath, backupPath);
            }
            File.Move(tempPath, grfPath);
            if (File.Exists(backupPath)) File.Delete(backupPath);
        }
        catch
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
            if (File.Exists(backupPath) && !File.Exists(grfPath))
                File.Move(backupPath, grfPath);
            throw;
        }
    }
}
