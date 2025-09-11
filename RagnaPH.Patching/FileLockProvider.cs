using System;
using System.IO;

namespace RagnaPH.Patching;

public sealed class FileLockProvider : ILockProvider
{
    private readonly string _lockPath;
    private FileStream? _stream;

    public FileLockProvider(string lockPath)
    {
        _lockPath = lockPath;
    }

    public bool TryAcquire()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_lockPath)!);
            _stream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        if (_stream != null)
        {
            try { File.Delete(_lockPath); } catch { }
        }
    }
}
