# 🌟 RagnaPH Launcher

![RagnaPH Logo](RagnaPH%20Launcher/Images/logo.png)

[![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-512BD4?logo=.net)](https://dotnet.microsoft.com/)

A polished Windows launcher and patcher for **RagnaPH** built with WPF. It displays the latest news, keeps the game files up to date, and launches the client with a single click.

## ✨ Features

- **Integrated News Feed** – Loads the [RagnaPH news page](https://ragna.ph/?module=news) inside the launcher and strips out navigation and footer elements for a clean look.
- **Automatic Patching** – Downloads a remote configuration and sequential patch list to keep the game client current.
- **Resilient Configuration** – Loads settings from a centralized URL and warns before falling back to a local `patchsettings.inf` if the remote file is unavailable.
- **Thor Archive Support** – Detects downloaded `.thor` patch archives and merges their contents into `data.grf`.
- **Command-line Patching** – Download and apply `.thor` patches directly using `--apply-patch`, removing the archive only when patching succeeds.
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

### Command-Line Patching

Download and apply a single `.thor` archive without opening the launcher UI:

```bash
RagnaPH\ Launcher.exe --apply-patch <patch-url> <path/to/data.grf>
```

The launcher downloads the patch to a temporary file, extracts it over the specified GRF directory, and deletes the patch archive only if the patch is applied successfully. It relies on the bundled **SharpCompress 0.40.0.0** library to extract the archive and merge it with the given GRF.

## 🛠️ Configuration

By default, the launcher points to `https://ragna.ph/patch/patchsettings.inf` for its behavior settings. If this remote configuration cannot be reached, it warns the user and falls back to a local `patchsettings.inf` file located next to the executable. Key values include:

| Section | Key | Description |
|--------|-----|-------------|
| `[Main]` | `allow` | Enable or disable patching |
| `[Main]` | `Force_Start` | Allow launching even if patching is disallowed |
| `[Main]` | `policy_msg` | Message shown when patching is disabled |
| `[Main]` | `file_url` | Base URL for patch files |
| `[Patch]` | `PatchList` | Name of the patch list file |
| `[Patch]` | `PatchLocation` | Subdirectory under `file_url` where patch files reside |

The patch list is fetched from `file_url + PatchList` and each patch file is downloaded from `file_url + PatchLocation + entry`.

#### Example `patchsettings.inf`

```
[Main]
allow = true
file_url = https://example.com/patch/

[Patch]
PatchList = patchlist.txt
PatchLocation = patches/
```

### Patch List Format

Each entry in `patchlist.txt` is processed in order and is relative to `PatchLocation`. The recommended format is a sequential number followed by the file name or subpath:

```
001 fix_camera.thor
002 interface.dll
```

Numbers are zero‑padded for readability but are parsed as integers. When a numbered entry succeeds, its number is recorded in `patch.ver` in the launcher directory. On the next run, any line with a number **less than or equal** to the value in `patch.ver` is skipped, allowing the client to avoid re‑applying patches that it already has. Lines without a leading number are always processed.

#### Adding a New Patch

1. Build the patch file (e.g. `my_fix.thor` or `data/skin.spr`).
2. Upload the file to your patch server under `file_url + PatchLocation`.
3. Append a new line to `patchlist.txt` with the next sequential number and the file’s relative path.
4. Upload the updated `patchlist.txt` alongside the patch file.
5. Clients will download the new list, apply any patch with a number greater than the one stored in `patch.ver`, and then update `patch.ver` to that number.

To force all numbered patches to re‑run, delete `patch.ver` or set its value to `0`.

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
