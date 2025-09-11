using System;

namespace RagnaPH.Patching;

public interface ILockProvider : IDisposable
{
    bool TryAcquire();
}
