# AssetInspector

Small CLI for inspecting mounted Squad assets through CUE4Parse. It searches virtual package paths,
dumps Unreal properties as JSON and resolves outgoing hard and soft object references.

```powershell
$env:SQUAD_PATH = 'C:\Program Files (x86)\Steam\steamapps\common\Squad'
$env:SQUAD_MAPPINGS = 'G:\jmap_dumper\SQUADGAME105.usmap'

dotnet run --project tools\AssetInspector -- find Anvil_Invasion_v1
dotnet run --project tools\AssetInspector -- inspect Gameplay_Layer_Data/Anvil_Invasion_v1.uasset --depth 1
dotnet run --project tools\AssetInspector -- inspect Gameplay_Layers/Anvil_Invasion_v1.umap --type SQWorldSettings --depth 0
dotnet run --project tools\AssetInspector -- inspect /Game/Maps/Anvil/Gameplay_Layer_Data/Anvil_Invasion_v1.Anvil_Invasion_v1 --depth 2 --output layer.json
dotnet run --project tools\AssetInspector -- metadata Anvil_Invasion_v1
```

`--depth` controls recursive reference resolution. `--limit` limits matching packages, properties and collection items.
