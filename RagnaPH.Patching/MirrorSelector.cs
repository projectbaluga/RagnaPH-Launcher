using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RagnaPH.Patching;

public sealed class MirrorSelector : IMirrorSelector
{
    private readonly List<PatchServer> _servers;
    private readonly TimeSpan _baseDelay;
    private int _currentIndex;
    private int _failures;

    public MirrorSelector(IPatchConfig config, TimeSpan? baseDelay = null)
    {
        _servers = config.PatchServers.ToList();
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
        _currentIndex = _servers.FindIndex(s => s.Name == config.PreferredPatchServer);
        if (_currentIndex < 0) _currentIndex = 0;
    }

    public PatchServer Current => _servers[_currentIndex];

    public PatchServer NextOnFailure()
    {
        _failures++;
        var delayMs = Math.Min(10000, _baseDelay.TotalMilliseconds * Math.Pow(2, _failures - 1));
        if (delayMs > 0)
            Thread.Sleep(TimeSpan.FromMilliseconds(delayMs));
        _currentIndex = (_currentIndex + 1) % _servers.Count;
        return Current;
    }

    public void Reset()
    {
        _failures = 0;
    }
}
