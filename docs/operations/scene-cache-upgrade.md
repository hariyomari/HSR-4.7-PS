# Scene Cache Metadata Upgrade

English | [简体中文](scene-cache-upgrade.zh-CN.md)

This guide is for operators who need to bring an existing `Config/Resource.bin` up to date for a new server build without rebuilding the whole cache from raw resources. It covers three targeted upgrade tools: scene group metadata, Divergent Universe data and Train Party data.

## Scene group metadata

Upgrade tool: [`scripts/SceneCacheUpgrade/`](../../scripts/SceneCacheUpgrade/)

### Background

`IsHoyoGroup` is stored as an appended nullable field: it is `null` in an old cache, and `false` when the raw resources omit the field. The 4.5 production build rejected a cache in which server-side atmosphere groups lacked this metadata; the current version no longer checks it at load time, so keep the metadata in place with this tool. A release must ship with its matching `Config/Resource.bin`.

### Prerequisites

Use this tool when you want to keep other modifications in the existing cache. Run it on a development machine that has raw resources of the same version.

### Upgrade

```powershell
dotnet run --project scripts/SceneCacheUpgrade/SceneCacheUpgrade.csproj -c Release -- Config/Resource.bin Resources artifacts/Resource.scene-upgraded.bin
```

The tool only fills in each group's `IsHoyoGroup`. It does not rebuild all game resources and does not read operational configuration or the database. It first verifies that the new snapshot, with this field reverted, is identical to the original snapshot, and only then writes the new output file. It stops when a server-side atmosphere group is missing its resources, when it meets abnormal data, or when the output file already exists; it never overwrites the input. Client groups or non-atmosphere groups that the old cache keeps but the current resources have removed are kept as they are, and their number is reported; the flag plays no part in the server-side load filtering of these groups.

### Verification, replacement and rollback

After the regressions pass, back up the existing cache, then copy the new file to `Config/Resource.bin`. To roll back, restore the program and the cache together; an old program cannot read a cache with the added field.

## Divergent Universe data

Upgrade tool: [`scripts/RogueTournCacheUpgrade/`](../../scripts/RogueTournCacheUpgrade/)

### Background

The data read by the Divergent Universe 4.6 flow is stored in the cache as appended members: `RoguePersonaLayerRoom`, `RoguePersonaRoomPreset`, `RoguePersonaRoomCompType`, `RoguePersonaStyleGift`, `RogueTournExpReward`, the event Act graphs (`RogueDialogue/RogueEventTourn*/Event*/Act*.json`), and the `RogueTournBuffGroup.TournMode` column. When an old cache is read, these members are empty. A full rebuild of the cache from raw resources of the same version includes them automatically; to add only these items, use this tool.

At runtime the server rejects the cache when the raw `RoguePersonaLayerRoom.json` exists but the cache lacks these tables.

### Prerequisites

The resource directory only needs the `ExcelOutput/*.json` files listed above, `Config/Level/Rogue/RogueNPC/` and `Config/Level/Rogue/RogueDialogue/RogueEventTourn*/` (for example, these files from the `OSPRODWin4.6.0` commit of TurnBasedGameData).

### Upgrade

```powershell
dotnet run --project scripts/RogueTournCacheUpgrade/RogueTournCacheUpgrade.csproj -c Release -- Config/Resource.bin <resource-dir> artifacts/Resource.tourn-upgraded.bin
```

The tool first confirms that the original cache does not contain this data yet, then verifies that the snapshot with the added content removed is byte-for-byte identical to the original snapshot, and finally writes the new file and reads it back to check it. It stops if any Act graph is missing, and it never overwrites the input.

### Verification, replacement and rollback

Before replacing the cache, verify the new file with `LoginSceneRegression -- --resource-cache <new-file>` and back up the original cache. To roll back, restore the program and the cache together.

### Caches from before the classic Simulated Universe blessing fix

Since the classic Simulated Universe blessing fix, `RogueBuffData` / `RogueBuffGroupData` store the classic and Divergent Universe subtypes as a MemoryPack union, and the cache format changed with it. A cache from before the fix cannot be read by the current program or by this tool (the log reports a `MemoryPackUnion` tag error, and the server then tries to rebuild from raw resources). Such a cache cannot be upgraded with this tool; regenerate it from complete raw resources of the same version. An environment that deploys only the cache must update the program and `Config/Resource.bin` together, and must also restore them together when rolling back.

## Train Party data

Upgrade tool: [`scripts/TrainPartyCacheUpgrade/`](../../scripts/TrainPartyCacheUpgrade/)

### Background

Train Party stars, build level, level rewards and the carriage floor save values depend on three pieces of appended data: `ShowBlockList` on each row of `TrainPartyAreaConfig` (room blocks such as `Bar_2F_Bedroom`; the room name maps to the floor save values `Progress_<room>` and `Build_<room>_Finished`), `ProgressBonusList` (stars added at progress thresholds), and the `TrainPartyRewardConfig` build level table. A full rebuild of the cache from raw resources of the same version includes them automatically; to add only these items, use this tool.

An old cache can still be read by the current program. Without these items, stars and build level are 0, there are no rewards to claim, and the carriage floor save values are not rewritten; the Train Party data check in `LoginSceneRegression` fails.

### Prerequisites

The resource directory only needs `ExcelOutput/TrainPartyAreaConfig.json` and `ExcelOutput/TrainPartyRewardConfig.json` (a client export is enough).

### Upgrade

```powershell
dotnet run --project scripts/TrainPartyCacheUpgrade/TrainPartyCacheUpgrade.csproj -c Release -- Config/Resource.bin <resource-dir> artifacts/Resource.trainparty.bin
```

The tool first confirms that the original cache does not contain this data yet and that every `RewardID` referenced by the reward table is in the cache. It then verifies that the snapshot with the added content removed is byte-for-byte identical to the original snapshot, and finally writes the new file and reads it back to check it. It never overwrites the input.

### Verification, replacement and rollback

Before replacing the cache, verify the new file with `LoginSceneRegression -- --resource-cache <new-file>` and back up the original cache. An old program cannot read a cache with the added fields; to roll back, restore the program and the cache together.

### Record: OSBETAWin4.7.51 (2026-11-14)

On the export in `artifacts/resources-4.7.51/Resources`, the tool added the room blocks and star thresholds of 6 areas and the 30-level reward table (30 stars in total). On the new cache: LoginSceneRegression 886/0, DailyActiveRegression 76/0, GridFightRegression 557/557. The backup of the cache before replacement is `artifacts/resources-4.7.51/Resource.bin.backup-20261114-before-trainparty.bin`; it is not distributed with the repository. Not verified on a real client.
