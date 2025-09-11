using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public interface IPatchListProvider
{
    Task<IReadOnlyList<PatchDescriptor>> FetchAsync(Uri plistUrl, CancellationToken ct);
}

public sealed record PatchDescriptor(int Index, string FileName, Uri RemoteUrl);
