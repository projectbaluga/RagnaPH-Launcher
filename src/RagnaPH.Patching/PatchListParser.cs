using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace RagnaPH.Patching;

/// <summary>
/// Parses Ragnarok Online plist.txt files into patch jobs.
/// </summary>
public static class PatchListParser
{
    private static readonly Regex IdFromFileName = new("\\d+", RegexOptions.Compiled);

    public static PatchPlan Parse(string content, string patchBaseUrl)
    {
        var jobs = new List<PatchJob>();

        foreach (var line in content.Split('\n'))
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;

            var parts = trimmed.Split('|');
            int id;
            string fileName;
            int index = 0;

            if (parts.Length > 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
            {
                id = parsedId;
                fileName = parts[1];
                index = 2;
            }
            else
            {
                var candidate = parts[0];
                var spaceIdx = candidate.IndexOf(' ');
                if (parts.Length == 1 && spaceIdx > 0 && int.TryParse(candidate.Substring(0, spaceIdx), NumberStyles.Integer, CultureInfo.InvariantCulture, out var spacedId))
                {
                    id = spacedId;
                    fileName = candidate.Substring(spaceIdx + 1);
                }
                else
                {
                    fileName = candidate;
                    var m = IdFromFileName.Match(fileName);
                    if (!m.Success)
                        throw new FormatException($"Cannot determine patch id from '{line}'.");
                    id = int.Parse(m.Value, CultureInfo.InvariantCulture);
                }
                index = 1;
            }

            long? size = null;
            string? sha = null;
            string? target = null;

            for (int i = index; i < parts.Length; i++)
            {
                var seg = parts[i];
                if (seg.StartsWith("size:"))
                    size = long.Parse(seg.Substring(5), CultureInfo.InvariantCulture);
                else if (seg.StartsWith("sha256:"))
                    sha = seg.Substring(7);
                else if (seg.StartsWith("target:"))
                    target = seg.Substring(7);
            }

            var url = new Uri(new Uri(patchBaseUrl), fileName);
            jobs.Add(new PatchJob(id, fileName, url, target, size, sha));
        }

        var highest = jobs.Count == 0 ? 0 : jobs.Max(j => j.Id);
        return new PatchPlan(highest, jobs);
    }
}
