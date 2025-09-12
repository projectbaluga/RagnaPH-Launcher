using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using RagnaPH.Launcher.Models;

namespace RagnaPH.Launcher.Services;

public static class PatchListParser
{
    private static readonly Regex LineRx =
        new(@"^\s*(\d+)\s+(.+?\.thor)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IEnumerable<PatchItem> Parse(string text, bool baseUrlEndsWithData)
    {
        foreach (var raw in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("//"))
                continue;
            int id;
            string file;

            var m = LineRx.Match(line);
            if (m.Success)
            {
                id = int.Parse(m.Groups[1].Value);
                file = m.Groups[2].Value;
            }
            else
            {
                var idMatch = Regex.Match(line, @"^\d+");
                if (!idMatch.Success)
                    continue;
                id = int.Parse(idMatch.Value);
                file = line.Substring(idMatch.Length).TrimStart(' ', '\t', '-', '_');

                var thorIdx = file.IndexOf(".thor", StringComparison.OrdinalIgnoreCase);
                if (thorIdx < 0)
                    continue;
                file = file.Substring(0, thorIdx + 5);
            }

            var rel = baseUrlEndsWithData ? file : $"data/{file}";
            yield return new PatchItem { Id = id, FileName = file, RelativePath = rel };
        }
    }
}

