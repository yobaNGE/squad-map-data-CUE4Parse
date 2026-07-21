# Squad Pipeline

Windows desktop tool for scanning **Squad** game content and exporting layer data with **CUE4Parse**.

Supports vanilla content and Steam Workshop mods, filtering by map, game mode and source, cached scans, and parallel export.

## Features

- Scans vanilla and enabled Steam Workshop content.
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
- Squad installed through Steam
- A matching `.usmap` mappings file

Generate the `.usmap` file with [jmap](https://github.com/trumank/jmap). For example, from a full-memory minidump:

```powershell
cargo run --release -- --minidump <dump.dmp> mappings.usmap
```

## Usage

1. Open **Settings**.
2. Select the Squad installation directory, `.usmap` file, and export directory.
3. Enable required Workshop mods and save the settings.
4. Click **Scan content**.
5. Filter and select layers, then click **Export selected**.

After a Squad or mod update, use **Rebuild** to refresh the cached catalog. Use **Clear cache** when cached data must be discarded completely.

## Limitations

Extraction is best effort, so partial failures are expected for unsupported game modes such as Insurgency, Destruction, and some custom modes.

Workshop mod support is still experimental and may miss some assets, including commander-related assets. Tested mainly with SPM and Steel Division.
