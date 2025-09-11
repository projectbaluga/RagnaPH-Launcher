using System;
using System.IO;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class LockProviderTests
{
    [Fact]
    public void ExclusivityWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var a = new FileLockProvider(path);
        Assert.True(a.TryAcquire());
        using var b = new FileLockProvider(path);
        Assert.False(b.TryAcquire());
    }
}
