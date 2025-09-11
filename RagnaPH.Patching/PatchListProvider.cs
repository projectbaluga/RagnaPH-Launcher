using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPH.Patching;

public sealed class PatchListProvider : IPatchListProvider
{
    private readonly HttpClient _client;

    public PatchListProvider(HttpClient? client = null)
    {
        _client = client ?? new HttpClient();
    }

    public async Task<IReadOnlyList<PatchDescriptor>> FetchAsync(Uri plistUrl, CancellationToken ct)
    {
        using var stream = await _client.GetStreamAsync(plistUrl, ct);
        using var reader = new StreamReader(stream);
        var list = new List<PatchDescriptor>();
        int lastIndex = 0;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                continue;
            var parts = trimmed.Split(' ', '\t');
            if (parts.Length < 2)
                throw new FormatException($"Malformed line: {line}");
            if (!int.TryParse(parts[0], out int index))
                throw new FormatException($"Invalid index in line: {line}");
            if (index <= lastIndex)
                throw new FormatException("Indices must be strictly increasing");
            var fileName = parts[1].Trim();
            var remote = new Uri(plistUrl, fileName);
            list.Add(new PatchDescriptor(index, fileName, remote));
            lastIndex = index;
        }
        return list;
    }
}
