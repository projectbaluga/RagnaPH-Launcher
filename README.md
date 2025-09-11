# 🌟 RagnaPH Launcher

![RagnaPH Logo](RagnaPH%20Launcher/Images/logo.png)

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=.net)](https://dotnet.microsoft.com/)

A polished Windows launcher and patcher for **RagnaPH** built with WPF. It displays the latest news, keeps the game files up to date, and launches the client with a single click.

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
2. Open the solution file `RagnaPH Launcher.sln` in Visual Studio.
3. Restore NuGet packages and build the project.
4. Run the generated executable or press <kbd>F5</kbd> in Visual Studio.

## 📁 Project Structure

- `RagnaPH Launcher/` – WPF front-end targeting .NET Framework 4.8.
- `src/RagnaPH.Patching/` – .NET 8 library that handles downloading patches, parsing lists, and tracking state.
- `tests/RagnaPH.Patching.Tests/` – xUnit tests for the patching engine.

### Patching Library Highlights

Key components inside `src/RagnaPH.Patching` include:

- `PatchConfigLoader` – reads `patcher.config.json` and ensures a valid setup.
- `HttpPatchSource` – queries mirrors for `plist.txt` and builds a patch plan.
- `PatchListParser` – converts patch list entries into strongly typed `PatchJob` records.
- `HttpPatchDownloader` – downloads archives with retry, size and checksum validation.
- `PatchStateStore` – tracks applied patch IDs in a JSON file.
- `PatchEngine` – orchestrates downloading and applying patches while raising progress events for the UI.

## 🧪 Testing

The test suite validates patch parsing, downloads, and state handling:

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

## 🤝 Contributing

Issues and pull requests are welcome! Feel free to fork the project and submit improvements.

## 📜 License

No explicit license has been provided. All rights reserved by the original author.

---
Made with ❤️ for the RagnaPH community.
