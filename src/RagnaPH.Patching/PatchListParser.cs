using System.Globalization;
using System.Text.RegularExpressions;

namespace RagnaPH.Patching;

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
            var parts = trimmed.Split(new[] { '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(p => p.Trim()).ToArray();
            int id; string fileName; int index;
            if (parts.Length > 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                id = parsed; fileName = parts[1]; index = 2;
            }
            else
            {
                var candidate = parts[0];
                if (parts.Length == 1)
                {
                    var m = Regex.Match(candidate, @"^(\d+)\s+(.+)$");
                    if (m.Success)
                    {
                        id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        fileName = m.Groups[2].Value; index = 1;
                    }
                    else
                    {
                        fileName = candidate;
                        var idMatch = IdFromFileName.Match(fileName);
                        if (!idMatch.Success)
                            throw new FormatException($"Cannot determine patch id from '{line}'.");
                        id = int.Parse(idMatch.Value, CultureInfo.InvariantCulture);
                        index = 1;
                    }
                }
                else
                {
                    fileName = candidate;
                    var idMatch = IdFromFileName.Match(fileName);
                    if (!idMatch.Success)
                        throw new FormatException($"Cannot determine patch id from '{line}'.");
                    id = int.Parse(idMatch.Value, CultureInfo.InvariantCulture);
                    index = 1;
                }
            }

            long? size = null; string? sha = null; string? target = null;
            for (int i = index; i < parts.Length; i++)
            {
                var seg = parts[i];
                if (seg.StartsWith("size:")) size = long.Parse(seg[5..], CultureInfo.InvariantCulture);
                else if (seg.StartsWith("sha256:")) sha = seg[7..];
                else if (seg.StartsWith("target:")) target = seg[7..];
            }
            var baseUrl = patchBaseUrl.EndsWith("/") ? patchBaseUrl : patchBaseUrl + "/";
            var decoded = Uri.UnescapeDataString(fileName);
            var url = new Uri(baseUrl + Uri.EscapeDataString(decoded));
            jobs.Add(new PatchJob(id, fileName, url, target, size, sha));
        }
        var highest = jobs.Count == 0 ? 0 : jobs.Max(j => j.Id);
        return new PatchPlan(highest, jobs);
    }
}
