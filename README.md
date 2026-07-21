# Squad Pipeline

Windows desktop tool for scanning **Squad** content and exporting layer data to JSON with **CUE4Parse**.

Supports the cooked Steam installation, Steam Workshop mods, and uncooked assets from the Squad SDK.

## Features

- Scans vanilla, enabled Steam Workshop content, or the Squad SDK.
- Automatically detects cooked and uncooked content layouts.
- Discovers content plugins from `Plugins/Mods` in the SDK.
- Builds a searchable catalog of layers with map, game mode, version, and source metadata.
- Caches scan results per content source to avoid parsing unchanged assets on every launch.
- Allows rebuilding or clearing the cache separately for vanilla content and individual mods.
- Supports configurable parallel export:
  - `1` worker uses the least memory.
  - `2` workers is a balanced default.
  - `3–8` workers are intended for faster CPUs and NVMe storage.
- Exports only selected layers and shows elapsed time, cache usage, and peak memory consumption.
- Automatically detects the Squad installation profile and Steam Workshop directory.

## Screenshots

Layer browser

<img width="1632" height="1013" alt="Layer browser" src="https://github.com/user-attachments/assets/4b32f75c-26e1-4a92-8573-078a457db3c6" />

Settings

<img width="1634" height="1016" alt="Settings" src="https://github.com/user-attachments/assets/35ce3723-128f-45da-9c14-e52caecdeda9" />

## Requirements

- Windows
- Squad installed through Steam, or the Squad SDK installed through Epic Games Launcher
- A matching `.usmap` mappings file for cooked game content

Generate the `.usmap` file with [jmap](https://github.com/trumank/jmap).

Mappings are not required for uncooked SDK assets.

## Usage

1. Open **Settings**.
2. Select the Squad installation or SDK directory and the export directory.
3. For cooked content, select a matching `.usmap` file.
4. Enable required Workshop mods or SDK plugins and save the settings.
5. Click **Scan content**.
6. Filter and select layers, then click **Export selected**.

After a Squad or mod update, use **Rebuild** to refresh the cached catalog. Use **Clear cache** when cached data must be discarded completely.

### Squad SDK

Select the SDK project root containing `SquadGame.uproject` and `Content`. The default location is usually:

```text
C:\Program Files\Epic Games\SquadEditor\Squad
```

The application reads uncooked project and plugin assets directly. SDK data can differ from cooked game exports when one of the installations is older.

## Build

```powershell
git submodule update --init --recursive
dotnet build Squad-pipeline-map-data-CUE4Parse.sln
```

Building requires the .NET 10 SDK on Windows.

## Limitations

Extraction is best effort, so partial failures are expected for unsupported game modes such as Insurgency, Destruction, and some custom modes.

Workshop mod support is still experimental and may miss some assets, including commander-related assets. Tested mainly with SPM and Steel Division.
