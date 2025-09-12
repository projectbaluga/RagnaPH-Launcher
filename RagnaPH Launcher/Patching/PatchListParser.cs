using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using RagnaPH.Launcher.Net;

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

            var parts = trimmed.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                parts[i] = parts[i].Trim();
            int id;
            string filePath;
            int index = 0;

            if (parts.Length > 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedId))
            {
                id = parsedId;
                filePath = parts[1];
                index = 2;
            }
            else
            {
                var candidate = parts[0];

                // Handle combined "id filename" segment even when additional metadata is present
                var m = Regex.Match(candidate, @"^(\d+)\s+(.+)$");
                if (m.Success)
                {
                    id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                    filePath = m.Groups[2].Value;
                }
                else
                {
                    filePath = candidate;
                    var idMatch = IdFromFileName.Match(filePath);
                    if (!idMatch.Success)
                        throw new FormatException($"Cannot determine patch id from '{line}'.");
                    id = int.Parse(idMatch.Value, CultureInfo.InvariantCulture);

                    // Strip the id prefix if it appears at the beginning of the file path
                    if (filePath.StartsWith(idMatch.Value, StringComparison.Ordinal))
                    {
                        filePath = filePath.Substring(idMatch.Value.Length).TrimStart(' ', '\t', '-', '_');
                    }

                    // Truncate anything following the .thor extension
                    var thorIdx = filePath.IndexOf(".thor", StringComparison.OrdinalIgnoreCase);
                    if (thorIdx >= 0)
                        filePath = filePath.Substring(0, thorIdx + 5);
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
                else if (seg.EndsWith(".thor", StringComparison.OrdinalIgnoreCase))
                    filePath = seg;
            }

            // Normalize the path without encoding; encoding is handled when
            // constructing the final URI.
            var normalizedPath = PatchNameUtils.NormalizePath(filePath);
            var url = PatchUrlBuilder.Build(new Uri(patchBaseUrl), normalizedPath);
            jobs.Add(new PatchJob(id, normalizedPath, url, target, size, sha));
        }

        var highest = jobs.Count == 0 ? 0 : jobs.Max(j => j.Id);
        return new PatchPlan(highest, jobs);
    }
}
