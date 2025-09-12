using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RagnaPH.Patching;
using Xunit;

namespace RagnaPH.Patching.Tests;

public class PatchEngineTests
{
    [Fact]
    public async Task AppliesPatchAndUpdatesState()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        var download = Path.Combine(root, "dl");
        Directory.CreateDirectory(download);
        var statePath = Path.Combine(root, "state.json");

        try
        {
            var grfFile = Path.Combine(root, "data.grf");
            File.WriteAllText(grfFile, "old");

            var config = new PatchConfig(
                new WebConfig(new(), 30, 1, new RetryConfig(1, Array.Empty<int>())),
                new PatchingConfig("data.grf", false, false, true, 0),
                new PathConfig(root, download, statePath));

            var store = new PatchStateStore(statePath);
            var downloader = new StubDownloader();
            StubGrfEditor? captured = null;
            var engine = new PatchEngine(downloader, config, store,
                () => new StubThorReader(),
                () => { captured = new StubGrfEditor(); return captured; });

            var job = new PatchJob(1, "patch1.thor", new Uri("http://example/patch1.thor"), null, null, null);
            var plan = new PatchPlan(1, new[] { job });

            await engine.ApplyPlanAsync(plan, CancellationToken.None);

            Assert.NotNull(captured);
            Assert.EndsWith(".new", captured!.OpenedPath); // temp file used
            Assert.True(captured.IndexRebuilt);
            Assert.Contains("file1.txt", File.ReadAllText(grfFile));
            Assert.False(File.Exists(grfFile + ".new"));
            Assert.False(File.Exists(grfFile + ".bak"));
            Assert.Contains(1, (await store.LoadAsync()).AppliedIds);
            Assert.Equal(1, (await store.LoadAsync()).LastAppliedId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private sealed class StubDownloader : IPatchDownloader
    {
        public Task<string> DownloadAsync(PatchJob job, CancellationToken ct)
        {
            var path = Path.Combine(Path.GetTempPath(), job.FileName);
            File.WriteAllText(path, "dummy");
            return Task.FromResult(path);
        }
    }

    private sealed class StubThorReader : IThorReader
    {
        public Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct)
            => Task.FromResult(new ThorManifest(null, false));

        public async IAsyncEnumerable<ThorEntry> ReadEntriesAsync(string thorPath, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new ThorEntry("file1.txt", ThorEntryKind.File, 0, 0, null,
                () => Task.FromResult<Stream>(new MemoryStream(new byte[] { 1, 2, 3 })));
            yield return new ThorEntry("remove.txt", ThorEntryKind.Delete, 0, 0, null,
                () => Task.FromResult<Stream>(Stream.Null));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class StubGrfEditor : IGrfEditor
    {
        public string? OpenedPath { get; private set; }
        public bool IndexRebuilt { get; private set; }
        private FileStream? _stream;

        public Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct)
        {
            OpenedPath = grfPath;
            _stream = new FileStream(grfPath, createIfMissing ? FileMode.OpenOrCreate : FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return Task.CompletedTask;
        }

        public async Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct)
        {
            if (_stream == null) throw new InvalidOperationException();
            _stream.Seek(0, SeekOrigin.End);
            using var writer = new StreamWriter(_stream, leaveOpen: true);
            await writer.WriteAsync(virtualPath);
            await writer.FlushAsync();
        }

        public Task DeleteAsync(string virtualPath, CancellationToken ct) => Task.CompletedTask;
        public Task RebuildIndexAsync(CancellationToken ct)
        {
            IndexRebuilt = true;
            return Task.CompletedTask;
        }
        public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
        public Task VerifyAsync(CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync()
        {
            _stream?.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

