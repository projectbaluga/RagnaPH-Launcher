using System;
using System.Collections.Generic;

namespace RagnaPH.Patching;

public interface IPatchConfig
{
    string DefaultGrfName { get; }
    bool InPlace { get; }
    bool CheckIntegrity { get; }
    bool CreateGrf { get; }
    string PreferredPatchServer { get; }
    IReadOnlyList<PatchServer> PatchServers { get; }
}

public sealed record PatchServer(string Name, Uri PlistUrl, Uri PatchUrl);
