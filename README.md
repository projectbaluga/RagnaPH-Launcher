# 🌟 RagnaPH Launcher

![RagnaPH Logo](RagnaPH%20Launcher/Images/logo.png)

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=.net)](https://dotnet.microsoft.com/)

A polished Windows launcher and patcher for **RagnaPH** built with WPF and a companion **.NET 8** patching library. It displays the latest news, keeps the game files up to date, and launches the client with a single click.

## 📚 Table of Contents

- [Features](#-features)
- [Getting Started](#-getting-started)
- [Usage](#-usage)
- [Project Structure](#-project-structure)
- [Patching Workflow](#-patching-workflow)
- [Testing](#-testing)
- [Configuration](#%EF%B8%8F-configuration)
- [Setting Up a Patch Server](#-setting-up-a-patch-server)
- [UI Overview](#-ui-overview)
- [Troubleshooting](#-troubleshooting)
- [Contributing](#-contributing)
- [License](#-license)

## ✨ Features

- **Integrated News Feed** – Loads the [RagnaPH news page](https://ragna.ph/?module=news) inside the launcher and strips out navigation and footer elements for a clean look.
- **Automatic Patching** – Downloads a remote configuration and sequential patch list to keep the game client current.
- **Verified Downloads** – `HttpPatchDownloader` retries failed downloads and validates file size and SHA-256 hashes before applying patches.
- **Patch State Persistence** – `PatchStateStore` records applied patch IDs in `patch_state.json` so the launcher only downloads missing updates.
- **Thor Archive Support** – Detects downloaded `.thor` patch archives and merges their contents into `data.grf`.
- **Progress Feedback** – Visual progress bar and status text during patching.
- **One‑Click Launch** – Starts `RagnaPH.exe` directly from the launcher once patching is complete.
- **Fail‑Safe Messaging** – Gracefully reports errors such as missing files, patch failures, or maintenance notices.

## 🚀 Getting Started

### Prerequisites

- Windows with [Visual Studio](https://visualstudio.microsoft.com/) 2019 or later
- **.NET Framework 4.8**
- Optional: [Mono](https://www.mono-project.com/) for experimental builds on other platforms

### Build & Run

1. Clone the repository:
   ```bash
   git clone https://github.com/your-user/RagnaPH-Launcher.git
   ```
2. Restore dependencies and build the solution.
   - **Visual Studio:** Open `RagnaPH Launcher.sln`, allow NuGet to restore packages, then build and run.
   - **Command line:** In a Developer Command Prompt run:
     ```cmd
     nuget restore
     msbuild "RagnaPH Launcher.sln" /p:Configuration=Release
     ```
3. Ensure `patcher.config.json` and any server-side `config.ini` are placed next to the built executable.
4. Launch `RagnaPH Launcher.exe` (or press <kbd>F5</kbd> in Visual Studio) to start patching.

## 💻 Usage

1. Place the launcher in the same directory as the RagnaPH game files.
2. Edit `patcher.config.json` and `config.ini` to point to your patch servers.
3. Run the launcher; it displays news, downloads missing patches, and enables **Launch Game** when ready.
4. Use **Check Files** to re‑validate game data if an update fails.

## 📁 Project Structure

Top-level layout:

- `RagnaPH Launcher/` – WPF front-end targeting .NET Framework 4.8.
- `src/` – source libraries.
  - `src/RagnaPH.Patching/` – .NET 8 library for downloading patches, parsing lists, and tracking state.
- `tests/` – test projects.
  - `tests/RagnaPH.Patching.Tests/` – xUnit tests for the patching engine.
- `patcher.config.json` – sample launcher configuration consumed by the patching library.
- `RagnaPH Launcher.sln` – Visual Studio solution file.
- `RagnaPH-Launcher-Patching-Prompt.md` – design prompt describing the patching requirements.

### Patching Library Highlights

Key components inside `src/RagnaPH.Patching` include:

- `PatchConfigLoader` – reads `patcher.config.json` and ensures a valid setup.
- `HttpPatchSource` – queries mirrors for `plist.txt` and builds a patch plan.
- `PatchListParser` – converts patch list entries into strongly typed `PatchJob` records.
- `HttpPatchDownloader` – downloads archives with retry, size and checksum validation.
- `PatchStateStore` – tracks applied patch IDs in a JSON file.
- `PatchEngine` – orchestrates downloading and applying patches while raising progress events for the UI.
- `GrfMerger` – applies THOR contents to GRF files using a safe, atomic merge process.

### Patching Workflow

1. **Configuration and Setup**  
   `PatchConfig` defines patch settings such as mirrors, retry policies, and GRF options. `PatchConfigLoader` reads `patcher.config.json` and ensures at least one server is present before returning the configuration.

2. **Discovering Available Patches**  
   `HttpPatchSource` cycles through the configured mirrors, retrieving `plist.txt` until one succeeds. `PatchListParser` splits each line and constructs a `PatchPlan` containing patch jobs and the highest remote patch ID.

3. **Downloading Patch Archives**  
   `HttpPatchDownloader` saves `.thor` files to a temporary folder, retrying failures with backoff. When size and SHA‑256 metadata are provided it verifies them before returning the path.

4. **Applying Patches**  
   `PatchEngine` orders jobs by ID, skips those already recorded, downloads each archive, reads its manifest via an `IThorReader`, selects the target GRF, and performs file additions or deletions. Optional index rebuilds and integrity checks run according to configuration before the job is marked applied.

5. **State Tracking**  
   `PatchStateStore` persists applied patch IDs atomically in JSON so future runs only download missing updates.

## 🧪 Testing

The test suite validates patch parsing, downloads, and state handling. Run it from the repository root with the .NET SDK installed:

```bash
dotnet test
```

## 🛠️ Configuration

The launcher downloads its behavior settings from a remote `config.ini`. Key values include:

| Section | Key | Description |
|--------|-----|-------------|
| `[Main]` | `allow` | Enable or disable patching |
| `[Main]` | `Force_Start` | Allow launching even if patching is disallowed |
| `[Main]` | `policy_msg` | Message shown when patching is disabled |
| `[Main]` | `file_url` | Base URL for patch files |
| `[Patch]` | `PatchList` | Name of the patch list file |

The patch list is fetched from `file_url + PatchList` and each entry is downloaded relative to the launcher’s directory.

### `patcher.config.json`

The patching engine also reads a local JSON file to determine patch servers and patch behaviour. A minimal example is:

```json
{
  "web": {
    "patchServers": [
      { "name": "primary", "plistUrl": "https://patch.ragna.ph/plist.txt", "patchUrl": "https://patch.ragna.ph/patches/" }
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

These values correspond to the `PatchConfig` model in `src/RagnaPH.Patching` and can be customised for different patch hosts and installation locations.

## 🌐 Setting Up a Patch Server

1. **Prepare Patch Files**  
   Create `.thor` archives for each update using a Thor-compatible packing tool.

2. **Create `plist.txt`**  
   List patches in order using the format `id,filename[,size][,sha256][,targetGrf]`. Example:

   ```
   1,patch_0001.thor,12345,0f4d...,data.grf
   2,patch_0002.thor
   ```

3. **Host Files**  
   Serve `plist.txt` and the patch archives over HTTP(S) using any static host (Nginx, Apache, S3, etc.).

4. **Configure the Launcher**  
   Add your server under `web.patchServers` in `patcher.config.json`:

   ```json
   {
     "name": "myserver",
     "plistUrl": "https://patch.example.com/plist.txt",
     "patchUrl": "https://patch.example.com/patches/"
   }
   ```

   Multiple entries provide mirrors; the launcher tries each until one returns a valid `plist.txt`.

5. **Deploy**  
   Upload the files and ensure the launcher points to the correct URLs. It will download `plist.txt`, compare applied IDs, and fetch any missing patches from your server.

## 📸 UI Overview

```
+------------------------------------------------------+
|          [ Logo ]                                     |
|------------------------------------------------------|
| [ News Browser (RagnaPH latest articles) ]            |
|------------------------------------------------------|
| [Progress Bar.................................100%]  |
| Patching: data.grf (100%)                            |
|                                                      |
| [Launch Game]   [Check Files]   [Exit]               |
+------------------------------------------------------+
```

## 🐞 Troubleshooting

- **Launcher can't find the game:** place the executable in the same folder as `RagnaPH.exe`.
- **Patching fails with network errors:** verify the patch server URLs in `config.ini` and `patcher.config.json`.
- **Checksum mismatch:** delete the affected patch from `patch_tmp` and restart the launcher.

## 🤝 Contributing

Issues and pull requests are welcome! Feel free to fork the project and submit improvements.

## 📜 License

No explicit license has been provided. All rights reserved by the original author.

---
Made with ❤️ for the RagnaPH community.
