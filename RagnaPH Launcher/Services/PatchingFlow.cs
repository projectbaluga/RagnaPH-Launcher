using System;
using System.Collections.Generic;
using System.Linq;
using RagnaPH.Launcher.Models;
using RagnaPH.Launcher.Net;

namespace RagnaPH.Launcher.Services;

/// <summary>
/// Simple patching flow helper that parses a plist and produces encoded patch URIs.
/// </summary>
public static class PatchingFlow
{
    public static IEnumerable<Uri> BuildPatchUris(string baseUrl, string plistContent)
    {
        var baseUri = new Uri(baseUrl);
        var baseHasData = baseUri.AbsolutePath.TrimEnd('/').EndsWith("/data", StringComparison.OrdinalIgnoreCase);
        return PatchListParser.Parse(plistContent, baseHasData)
            .OrderBy(x => x.Id)
            .Select(p => PatchUrlBuilder.Build(baseUri, p.RelativePath));
    }
}

