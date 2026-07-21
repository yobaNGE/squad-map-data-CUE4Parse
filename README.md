# Squad Pipeline

Windows desktop tool for scanning **Squad** game content and exporting layer data with **CUE4Parse**.

Supports vanilla content and Steam Workshop mods, filtering by map, game mode and source, cached scans, and parallel export.

## Screenshots

Layer browser
<img width="1632" height="1013" alt="image" src="https://github.com/user-attachments/assets/4b32f75c-26e1-4a92-8573-078a457db3c6" />

Settings
<img width="1634" height="1016" alt="image" src="https://github.com/user-attachments/assets/35ce3723-128f-45da-9c14-e52caecdeda9" />

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

After a Squad or mod update, use **Rebuild** to refresh the cached catalog.

## Limitations
Uses best effort to extract data, so partial fail is expected for non supported gamemodes. Like insurgency and destruction. And maybe custom gamemodes.
Mods support is wobbly, it might miss commander asset. Tested on SPM and SD.
