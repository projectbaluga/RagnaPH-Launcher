# RagnaPH Launcher — **Comprehensive Patching & Patch-Parsing Prompt**  
_Use this .md prompt with your code assistant to fully implement a safe, rpatchur-style patcher inside `projectbaluga/RagnaPH-Launcher`._

---

## 🎯 Objectives

Implement a production-grade **patching engine** and **patch parser** for RagnaPH Launcher that:

- Discovers patches from **one or more mirrors** (via a `plist.txt` patch list).
- Downloads and applies **`.thor` patch archives**.
- **Merges** THOR contents into **`data.grf`** (and/or writes to disk) safely.
- Provides **CLI**, **UI progress callbacks**, **resume/retry**, **idempotency**, and **integrity checks**.
- Eliminates **GRF corruption** risk via **non-in-place** atomic apply with rollback.
- Supports **manual patching**: `--apply-patch <url or path-to.thor>` and removes the archive after success.

Non-goals: game balancing, server patch generation tooling, and AV/whitelisting.

---

## 🧱 Repo Integration

- **Language/stack**: C# (.NET 6+).  
- **Projects to (add/update)**:
  - `src/RagnaPH.Launcher` (WPF UI) — **use MVVM** for progress binding.
  - `src/RagnaPH.Patching` (new class library) — **patching engine + parsing**.
  - `tests/RagnaPH.Patching.Tests` — **unit + integration tests**.

Keep the patcher **UI-agnostic**; WPF subscribes to events.

---

## 🗂️ Configuration Model (JSON)

Create `patcher.config.json` at app root. Example:

```json
{
  "web": {
    "patchServers": [
      {
        "name": "primary",
        "plistUrl": "https://patch.ragna.ph/plist.txt",
        "patchUrl": "https://patch.ragna.ph/patches/"
      },
      {
        "name": "mirror-1",
        "plistUrl": "https://mirror1.ragna.ph/plist.txt",
        "patchUrl": "https://mirror1.ragna.ph/patches/"
      }
    ],
    "timeoutSeconds": 30,
    "maxParallelDownloads": 3,
    "retry": { "maxAttempts": 4, "backoffSeconds": [1, 2, 5, 10] }
  },
  "patching": {
    "defaultTargetGrf": "data.grf",
    "inPlace": false,
    "checkIntegrity": true,
    "createGrf": true,
    "enforceFreeSpaceMB": 512
  },
  "paths": {
    "gameRoot": ".",
    "downloadTemp": "patch_tmp",
    "appliedIndex": "patch_state.json"
  }
}
```

---

## 🧩 Data Contracts & Interfaces

Create in `RagnaPH.Patching`:

```csharp
namespace RagnaPH.Patching;

public record PatchConfig(WebConfig Web, PatchingConfig Patching, PathConfig Paths);
public record WebConfig(List<PatchServer> PatchServers, int TimeoutSeconds, int MaxParallelDownloads, RetryConfig Retry);
public record PatchServer(string Name, string PlistUrl, string PatchUrl);
public record RetryConfig(int MaxAttempts, int[] BackoffSeconds);
public record PatchingConfig(string DefaultTargetGrf, bool InPlace, bool CheckIntegrity, bool CreateGrf, int EnforceFreeSpaceMB);
public record PathConfig(string GameRoot, string DownloadTemp, string AppliedIndex);

public record PatchPlan(int HighestRemoteId, IReadOnlyList<PatchJob> Jobs);
public record PatchJob(int Id, string FileName, Uri DownloadUrl, string? TargetGrf, long? SizeBytes, string? Sha256);

public record PatchState(int LastAppliedId, HashSet<int> AppliedIds);

public interface IPatchSource { Task<PatchPlan> GetPlanAsync(CancellationToken ct); }
public interface IPatchDownloader { Task<string> DownloadAsync(PatchJob job, CancellationToken ct); } // returns path to .thor
public interface IThorReader : IAsyncDisposable {
    Task<ThorManifest> ReadManifestAsync(string thorPath, CancellationToken ct);
    IAsyncEnumerable<ThorEntry> ReadEntriesAsync(string thorPath, CancellationToken ct);
}
public record ThorManifest(string? TargetGrf, bool IncludesChecksums);
public record ThorEntry(string VirtualPath, ThorEntryKind Kind, long UncompressedSize, long CompressedSize, string? Sha256, Func<Task<Stream>> OpenStreamAsync);
public enum ThorEntryKind { File, Delete, Directory }

public interface IGrfEditor : IAsyncDisposable {
    Task OpenAsync(string grfPath, bool createIfMissing, CancellationToken ct);
    Task AddOrReplaceAsync(string virtualPath, Stream content, CancellationToken ct);
    Task DeleteAsync(string virtualPath, CancellationToken ct);
    Task RebuildIndexAsync(CancellationToken ct);
    Task FlushAsync(CancellationToken ct);
    Task VerifyAsync(CancellationToken ct); // optional integrity
}

public interface IPatchEngine {
    event EventHandler<PatchProgressEventArgs> Progress;
    Task ApplyPlanAsync(PatchPlan plan, CancellationToken ct);
    Task ApplySingleAsync(PatchJob job, CancellationToken ct); // used by CLI --apply-patch
}
public record PatchProgressEventArgs(
    string Phase,            // "Download", "Apply", "Rebuild", "Done", "Error"
    int? CurrentId,
    double? Percent,         // 0..100
    long? BytesDownloaded,
    long? BytesTotal,
    string? Message);
```
> **Note:** Keep `IGrfEditor` abstract. If a GRF library is unavailable, generate a minimal adapter now and stub integrity methods with TODOs; the rest of the engine must still compile.

---

## 🔎 Patch Discovery & Parsing

### `plist.txt` parser
- Format: newline-separated patch descriptors. Support both:
  - `123|patch_0123.thor|sha256:...|size:...|target:data.grf`
  - `patch_0123.thor` (infer ID from digits)
- **Rules**
  - Ignore comments `# ...`
  - Sort by **ID ascending**
  - De-duplicate by **ID** and **filename**
  - Map each to `PatchJob` with resolved `DownloadUrl = PatchServer.patchUrl + filename`

### Applied index (`patch_state.json`)
```json
{ "lastAppliedId": 0, "appliedIds": [] }
```
- Update atomically after each successful job.
- Never re-apply an already applied ID.

---

## 📦 THOR Archive Support

Implement `IThorReader` that can:
- Read **target GRF hint** (if present). If missing, use `patching.defaultTargetGrf`.
- Iterate entries **without loading all into memory** (streaming).
- Validate **SHA-256** (if provided in entry or side manifest).
- Disallow **path traversal** (`..`, absolute paths). Normalize to GRF virtual paths (e.g., `data\texture\...` → `data/texture/...`).
- Recognize **deletes** if the THOR includes delete entries (use a simple convention: entries with `Kind=Delete` or sidecar list like `delete.lst`).

> If the THOR format requires a specific library, create a clean boundary and place all dependencies in `RagnaPH.Patching.Thor`. The rest of the engine must not depend on THOR specifics.

---

## 🧰 GRF Merge (No-Corruption Design)

- **Default:** `inPlace=false` for safety.
- Algorithm:
  1. **Open source GRF** (`data.grf`) read-only; copy or create **temp GRF**: `data.grf.apply.tmp`.
  2. Apply all THOR entries to **temp** via `IGrfEditor`:
     - `File` → `AddOrReplaceAsync(virtualPath, stream)`
     - `Delete` → `DeleteAsync(virtualPath)`
  3. `RebuildIndexAsync()`; `FlushAsync()`.
  4. Optional `VerifyAsync()` (index + random sample hashing).
  5. **Atomic swap**:
     - `data.grf.old` ← rename `data.grf` (if exists)
     - rename `data.grf.apply.tmp` → `data.grf`
     - delete `data.grf.old` on success
- **In-place mode** (`inPlace=true`): write directly, still `RebuildIndexAsync()` and `FlushAsync()`. Use only when user opts-in.
- Enforce **free space** check from config.

---

## 🌐 Downloader

- Streaming HTTP client with:
  - **Retry/backoff** (config).
  - **Range requests** for resume (if server supports).
  - **Temp file** pattern: `patch_tmp/<id>_<name>.thor.part` → rename on success.
- Emit granular progress: bytes received / total (if known).

---

## 🧭 Engine Flow

```text
Load config → Ensure folders → Lock (named mutex "RagnaPH.Patcher")
→ Load or create patch_state.json
→ For each server (primary → mirrors):
   - Download plist.txt
   - Build PatchPlan (filter out already applied)
   - For each PatchJob in order:
       Download .thor → ThorReader.ReadManifest → Determine targetGrf
       Open IGrfEditor(targetGrf, createIfMissing)
       Apply to temp GRF (non-in-place)
       Update patch_state.json atomically
       Delete downloaded .thor (if CLI/manual flag says so)
→ Release lock → Done
```

**Locking:** refuse to patch if `RagnaPH.exe` (game) is running. Detect via process name.

---

## 🖥️ WPF Integration (MVVM)

- `PatchViewModel` subscribes to `IPatchEngine.Progress`.
- Display:
  - Overall phase and %.
  - Current patch ID and file.
  - Download bar (bytes/total) and speed.
  - Log panel (append messages).
- Buttons: **Check for updates**, **Apply updates**, **Apply local .thor…**.

---

## 🧪 Tests (must pass)

1. **Plist parsing**
   - With comments, mixed formats, duplicates, unordered IDs.
2. **State idempotency**
   - Re-run with same plist; nothing reapplied.
3. **Downloader resume**
   - Simulate interrupted download; ensure resume/complete.
4. **THOR streaming**
   - Large entry (>200MB) applied without OOM.
5. **Path traversal defense**
   - Entries containing `..\` are rejected.
6. **GRF safety**
   - Simulate power loss between `Flush` and swap: original `data.grf` remains valid.
7. **Manual patch**
   - `--apply-patch` local file: applied + file deleted if `--remove` provided.
8. **Mirror fallback**
   - Primary down → mirror used.
9. **Free-space guard**
   - Insufficient disk → refuses with clear message.

Provide fixtures:
- `tests/fixtures/plist_basic.txt`
- `tests/fixtures/plist_mixed.txt`
- `tests/fixtures/thor_small.thor`
- `tests/fixtures/thor_large_simulated.thor` (generate on the fly)
- `tests/fixtures/thor_traversal.thor`

---

## 🛡️ Security & Safety

- Reject unsigned sources unless HTTPS and domain matches allow-list.
- Optional **signature check** (RSA public key) for future: design hooks now.
- Sanitize all paths; never write outside game root.
- Maintain **transaction logs** per patch ID for audit.

---

## 🧭 CLI (Console)

Add to launcher exe (or separate `RagnaPH.Patcher.Cli`):

```
RagnaPH.Launcher.exe --check
RagnaPH.Launcher.exe --apply
RagnaPH.Launcher.exe --apply-patch "C:\Downloads\fix_0123.thor" --remove
RagnaPH.Launcher.exe --apply-patch "https://patch.ragna.ph/patches/fix_0123.thor" --remove
RagnaPH.Launcher.exe --config ".\patcher.config.json" --mirror mirror-1
RagnaPH.Launcher.exe --in-place
```

**Behavior**
- `--check`: downloads `plist.txt`, prints pending IDs, exits 0 if none.
- `--apply`: full flow using config and state.
- `--apply-patch`: skips plist; applies exactly one THOR; if `--remove`, deletes the archive on success.
- All commands return **non-zero** on failure.

---

## 📄 Sample `plist.txt` Variants

```
# Simple
001|patch_0001.thor|size:1048576|target:data.grf
002|patch_0002.thor

# Minimal
patch_0003.thor
# Mixed with checksum
004|patch_0004.thor|sha256:3e7f...|target:data.grf
```

Parsing rules:
- Extract numeric **ID** (prefer field 0; fallback to digits in filename).
- `size`, `sha256`, `target` are optional.

---

## 🔧 Logging

Central logger writing to `logs/patcher-YYYYMMDD.log`:
- Timestamp, level, patch ID, phase, message.
- At INFO: high-level steps.
- At DEBUG: URLs (without secrets), sizes, durations.
- At ERROR: exception + last step.

---

## 📦 Implementation Tasks (Actionable)

1. **Config loader** (`PatchConfigLoader`) with schema validation + sensible defaults.
2. **Plist service** (`HttpPatchSource`) → `PatchPlan`.
3. **State store** (`PatchStateStore`) with atomic update (`.new` → replace).
4. **Downloader** (`HttpPatchDownloader`) with resume + progress.
5. **THOR reader** (`ThorReader`):
   - Streaming entries; virtual path normalization; SHA-256 verify.
6. **GRF editor adapter** (`GrfEditorAdapter`) implementing `IGrfEditor`.
7. **Merger** (`GrfMerger`) with temp-file apply and atomic swap.
8. **Engine** (`PatchEngine`) orchestrating flow + events.
9. **CLI** commands.
10. **WPF** bindings and minimal UI.
11. **Test suite** + GitHub Actions workflow to run tests on PRs.

---

## ✅ Acceptance Criteria

- From a clean install, clicking **“Apply Updates”**:
  - Reads `plist.txt`, downloads pending patches, applies to **temp GRF**, atomically swaps, updates `patch_state.json`, and shows **Success**.
- Re-running with no new patches prints **“Up to date”** (CLI) / disables button (UI).
- **Manual THOR** via CLI applies and optionally deletes the file.
- **No GRF corruption** after forced crash during apply: original GRF loads fine.
- Mirrors work; download resumes; retries/backoff are visible in logs.
- **Zero path traversal**; entries outside GRF namespace are rejected.
- **Unit tests** pass locally and in CI.

---

## 📌 Coding Standards

- Nullable reference types **enabled**.
- `async`/`await` everywhere for I/O.
- Dispose all streams with `await using`.
- Public API XML-doc for all patching surfaces.
- No blocking UI thread; use `IProgress<T>` or events.

---

## 🧭 Developer Notes to the Code Assistant

- Generate **compilable** C# with projects, namespaces, and file layout.
- If no GRF library is available, **scaffold** `IGrfEditor` with a file-backed mock and leave clearly marked TODOs for actual GRF internals; ensure the rest of the pipeline (download → temp apply → atomic swap) is fully implemented.
- Provide **at least one** working `GrfEditorAdapter` implementation path using an **open-source GRF lib** if accessible via NuGet; otherwise, keep the adapter interface stable.
- Include **example WPF XAML** and ViewModel wiring for progress bars and status text.
- Provide **unit tests** for parsing, state, and safety edges; **integration test** that applies a synthetic THOR with dummy entries to a mock GRF.

---

## 🧪 Post-Gen Sanity Checklist (run locally)

- `dotnet build` succeeds.
- `dotnet test` passes.
- `RagnaPH.Launcher.exe --check` against a test plist prints pending IDs.
- `--apply` creates/updates `data.grf` without corruption.
- WPF shows progress and completion.

---

**End of prompt.**  
_This document is the single source of truth for the feature. Generate code, tests, and minimal UI according to the above. Do not ask questions—make reasonable assumptions and ship a working baseline._
