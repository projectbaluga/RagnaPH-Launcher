using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RagnaPH.Launcher.Models;

namespace RagnaPH.Launcher.Services;

public static class PatchListParser
{
    private static readonly Regex LineRx =
        new(@"^\s*(\d+)\s+(.+?\.thor)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<PatchItem> Parse(string text, bool baseUrlEndsWithData)
    {
        foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                continue;
            var m = LineRx.Match(line);
            if (!m.Success) continue;
            var id = int.Parse(m.Groups[1].Value);
            var file = m.Groups[2].Value;

            // Build relative path: if base ends with /data/, use filename only; else prefix "data/"
            var rel = baseUrlEndsWithData ? file : $"data/{file}";
            yield return new PatchItem { Id = id, FileName = file, RelativePath = rel };
        }
    }
}

