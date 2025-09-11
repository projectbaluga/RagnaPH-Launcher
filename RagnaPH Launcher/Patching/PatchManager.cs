using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RagnaPHPatcher.Patching
{
    public class PatchManager
    {
        private readonly PatchConfig _config;
        private readonly string _baseDir;

        public PatchManager(PatchConfig config, string baseDir)
        {
            _config = config;
            _baseDir = baseDir;
            Directory.CreateDirectory(Path.Combine(_baseDir, _config.PatchDirectory));
        }

        public async Task UpdateAsync(IProgress<string>? progress = null, CancellationToken token = default)
        {
            string? server = await SelectServerAsync(progress, token);
            if (server == null)
            {
                progress?.Report("No patch server available.");
                return;
            }

            progress?.Report($"Using {server}");
            string listUrl = new Uri(new Uri(server), _config.PatchList).ToString();
            string listContent = await DownloadStringAsync(listUrl, token);
            var patches = ParsePatchList(listContent);

            string cachePath = Path.Combine(_baseDir, _config.CacheFile);
            int lastPatch = 0;
            if (File.Exists(cachePath) && int.TryParse(File.ReadAllText(cachePath), out int cacheVal))
            {
                lastPatch = cacheVal;
            }

            foreach (var patch in patches.Where(p => p.Id > lastPatch))
            {
                token.ThrowIfCancellationRequested();
                progress?.Report($"Downloading patch {patch.Id}...");
                string url = new Uri(new Uri(server), patch.FileName).ToString();
                string dest = Path.Combine(_baseDir, _config.PatchDirectory, patch.FileName);
                await DownloadFileAsync(url, dest, token);
                progress?.Report($"Applying patch {patch.Id}...");
                await ApplyPatchAsync(dest, token);
                File.WriteAllText(cachePath, patch.Id.ToString());
            }

            progress?.Report("Patching complete.");
        }

        private async Task<string?> SelectServerAsync(IProgress<string>? progress, CancellationToken token)
        {
            foreach (var url in _config.PatchServers)
            {
                try
                {
                    progress?.Report($"Checking {url}...");
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(5);
                    var resp = await client.GetAsync(new Uri(new Uri(url), _config.PatchList), token);
                    if (resp.IsSuccessStatusCode)
                    {
                        return url.TrimEnd('/') + "/";
                    }
                }
                catch
                {
                    // Ignore and try next
                }
            }
            return null;
        }

        private static async Task<string> DownloadStringAsync(string url, CancellationToken token)
        {
            using var client = new HttpClient();
            return await client.GetStringAsync(url, token);
        }

        private static async Task DownloadFileAsync(string url, string destination, CancellationToken token)
        {
            using var client = new HttpClient();
            using var response = await client.GetAsync(url, token);
            response.EnsureSuccessStatusCode();
            using var fs = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            await response.Content.CopyToAsync(fs);
        }

        private static IEnumerable<PatchInfo> ParsePatchList(string content)
        {
            var list = new List<PatchInfo>();
            using var reader = new StringReader(content);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                if (int.TryParse(parts[0], out int id))
                {
                    list.Add(new PatchInfo { Id = id, FileName = parts[1].Trim() });
                }
            }
            return list;
        }

        private async Task ApplyPatchAsync(string zipPath, CancellationToken token)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string filePath = Path.Combine(_baseDir, entry.FullName);
                string? directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                entry.ExtractToFile(filePath, true);
            }
            await Task.CompletedTask;
        }
    }
}
