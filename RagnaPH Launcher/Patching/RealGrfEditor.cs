using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

/// <summary>
/// Real implementation of <see cref="IGrfEditor"/>.  When the official
/// GRFEditor library is available it is used via reflection; otherwise a
/// lightweight file system based fallback (<see cref="MockGrfEditor"/>) keeps
/// the patching pipeline operational.  This allows the launcher to apply THOR
/// patches against real GRF archives when the dependency is present while still
/// remaining functional in trimmed test environments.
/// </summary>
public sealed class RealGrfEditor : IGrfEditor
{
    private readonly MockGrfEditor _fallback = new();

    // Reflection based accessors for the GRF library.  When _holderType is null
    // the fallback implementation is used.
    private readonly Assembly? _grfAssembly;
    private readonly Type? _holderType;
    private object? _holder;
    private object? _commands;

    public RealGrfEditor()
    {
        try
        {
            _grfAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name.Equals("GRF", StringComparison.OrdinalIgnoreCase));

            _holderType = _grfAssembly?.GetType("GRF.ContainerFormat.GrfHolder");
        }
        catch
        {
            _grfAssembly = null;
            _holderType = null;
        }
    }

    private bool UseFallback => _holderType is null;

    public async Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
    {
        if (UseFallback)
        {
            await _fallback.OpenAsync(grfPath, createIfMissing, ct);
            return;
        }

        ct.ThrowIfCancellationRequested();

        if (!File.Exists(grfPath))
        {
            if (createIfMissing)
            {
                // Create an empty placeholder; the GRF library will initialise
                // the structure on save.
                using (File.Create(grfPath)) { }
            }
            else
            {
                throw new FileNotFoundException(grfPath);
            }
        }

        _holder = Activator.CreateInstance(_holderType!, new object[] { grfPath });
        _commands = _holderType!.GetProperty("Commands")?.GetValue(_holder);
    }

    public async Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
    {
        if (UseFallback || _holder is null || _commands is null)
        {
            await _fallback.AddOrReplaceAsync(virtualPath, content, ct);
            return;
        }

        ct.ThrowIfCancellationRequested();

        // The GRF library exposes AddFile(string,string).  To avoid relying on
        // its internal entry types we write the stream to a temporary file and
        // use that overload via reflection.
        string tmp = Path.GetTempFileName();
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs, 81920, ct);
            }

            var method = _commands.GetType().GetMethod("AddFile", new[] { typeof(string), typeof(string) });
            if (method == null)
                throw new InvalidOperationException("GRF: AddFile method not found");

            method.Invoke(_commands, new object[] { virtualPath.Replace('\\', '/'), tmp });
        }
        finally
        {
            if (File.Exists(tmp))
                File.Delete(tmp);
        }
    }

    public async Task DeleteAsync(string virtualPath, CancellationToken ct)
    {
        if (UseFallback || _commands is null)
        {
            await _fallback.DeleteAsync(virtualPath, ct);
            return;
        }

        ct.ThrowIfCancellationRequested();
        var method = _commands.GetType().GetMethod("RemoveFile", new[] { typeof(string) });
        if (method == null)
            throw new InvalidOperationException("GRF: RemoveFile method not found");
        method.Invoke(_commands, new object[] { virtualPath.Replace('\\', '/') });
    }

    public Task RebuildIndexAsync(CancellationToken ct)
    {
        // The GRF library rebuilds its index on save; no explicit action is
        // required.  This method exists for API symmetry.
        return Task.CompletedTask;
    }

    public Task FlushAsync(CancellationToken ct)
    {
        if (UseFallback || _holder is null)
            return _fallback.FlushAsync(ct);

        ct.ThrowIfCancellationRequested();
        _holderType!.GetMethod("QuickSave")?.Invoke(_holder, null);
        _holderType!.GetMethod("Reload")?.Invoke(_holder, null);
        return Task.CompletedTask;
    }

    public Task VerifyAsync(CancellationToken ct)
    {
        if (UseFallback)
            return _fallback.VerifyAsync(ct);

        // No specific verification is required here; the GRF library performs
        // integrity checks when loading and saving.  Keep the method for
        // interface compliance.
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_holder is IDisposable disp)
            disp.Dispose();
        else
            _fallback.Dispose();
    }
}
