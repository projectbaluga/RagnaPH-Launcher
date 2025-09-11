using System;
using System.Collections.Generic;
using System.IO;

namespace RagnaPH.Patching;

public interface IThorReader : IDisposable
{
    ThorTarget Target { get; }
    string? TargetGrfName { get; }
    IReadOnlyList<ThorEntry> Entries { get; }
    bool TryGetSha256(out byte[] hash);
}

public enum ThorTarget { Grf, Filesystem }

public sealed record ThorEntry(string PathInArchive, long UncompressedSize, Func<Stream> OpenStream);
