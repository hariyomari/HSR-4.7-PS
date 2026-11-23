# Rebuilding the Resource Cache from a Client Export

English | [简体中文](resource-cache-from-client-export.zh-CN.md)

This guide is for operators who have only the game client, without complete raw resources of the same version, and need a new `Resource.bin`. It exports the client's DesignData, fills the parts the export lacks from the existing cache, and verifies the result before it replaces anything.

## Overview and prerequisites

Without complete raw resources of the same version, only a client, use the `res` command of DemiurgeCSharp, a separate tool that dumps the client statically, to export the client's DesignData, then fill the parts missing from the export with the existing cache to obtain a new `Resource.bin`. The whole process runs in an isolated directory: it does not read the player database, does not write to `Config/` and does not overwrite the existing cache. Put the export directory under `artifacts/`, which Git already ignores.

When the raw resources are complete, this process is not needed: generate the cache directly with `--cache-out` as described in [Raw resource loading regression](../testing/resource-regression.md).

## 1. Export

```powershell
demiurge res all --game <client-root> --out artifacts/<name>/Resources --reports artifacts/<name>/reports
```

The client root is the directory that contains `GameAssembly.dll`. `reports/` holds one TSV per resource block; the status column is one of `ok`, `partial`, `fail`, `empty`, `noroot`.

## 2. Supplement the path list

The client's resource index stores only path hashes. The dumper needs the path to name a resource, and its built-in list does not contain the paths of a new version, so those resources are skipped as unnamed. [`scripts/ResourceNameHarvest/`](../../scripts/ResourceNameHarvest/) collects candidate paths from an existing export: every resource path referenced in the exported files, all table names the server loads, the `MissionInfo` path of every main mission in the mission table, and the floor file path of every floor listed for each plane in `MazePlane` (no file references floor paths, so they can only be derived as `P<plane>_F<floor>`; a floor file in turn references its groups). The dumper hashes each candidate and uses only those that actually exist in the index, so a wrong guess costs nothing.

```powershell
demiurge res keys --game <client-root> > artifacts/<name>/names/keys-0.tsv
dotnet run --project scripts/ResourceNameHarvest/ResourceNameHarvest.csproj -c Release -- artifacts/<name>/names/keys-0.tsv artifacts/<name>/Resources artifacts/<name>/names/paths-1.txt
demiurge res extract --game <client-root> --out artifacts/<name>/Resources --reports artifacts/<name>/reports --namemap artifacts/<name>/names/paths-1.txt
demiurge res keys --game <client-root> --namemap artifacts/<name>/names/paths-1.txt > artifacts/<name>/names/keys-1.tsv
```

Newly exported files reference more paths, so collect again with the new `keys` list and export again, until the number of named resources reported by `keys` stops increasing. `--namemap` replaces the built-in list, so the tool also writes the already named paths back into the list.

## 3. Build and fill gaps

[`scripts/ResourceCacheFill/`](../../scripts/ResourceCacheFill/) loads the export directory with the production loader in an isolated process, then fills gaps from the existing cache:

```powershell
dotnet run --project scripts/ResourceCacheFill/ResourceCacheFill.csproj -c Release -- artifacts/<name>/Resources Config/Resource.bin artifacts/<name>/Resource.new.bin artifacts/<name>/fill-report
```

- A table that exists in the export but fails to parse makes the tool exit with an error: that is a loader problem to fix (for example a new enum member), not something to cover with the old cache. A missing file only produces a warning, recorded in the report.
- Dictionaries are filled row by row by key: rows present in the export come from the export; rows absent from the export but present in the old cache are added. Lists have no key, so a list is taken whole from the old cache only when the export's list is empty. A single object or numeric value is taken from the old cache only when the export leaves it at its default value.
- GridFight tables always use only the export, with no rows filled from the old cache; the number of extra rows in the old cache is listed in the report. The Rogue, Daily and MainMissionSchedule tables that are lodged in `GridFightRuntimeDataSnapshot` because of the MemoryPack member limit are not subject to this rule.
- The four phone message tables (`MessageContactsConfigData`, `MessageGroupConfigData`, `MessageSectionConfigData`, `MessageItemConfigData`) also use only the export. The client resolves the contacts, message groups and sections sent by the server against its own tables; when a section cannot be found, `MessageModule._InitSection` throws a null reference and the handling of the whole `GetNpcMessageGroupScRsp` stops. Rows that the client has already deleted must therefore not be filled in from the old cache.
- Version-scoped compatibility rows are not copied from a donor cache or written as synthesized rows. Native rows in the fresh export stay untouched. For a verified 4.7.51 export or cache that lacks Star Lotto, set `Path.ResourceVersion` to `"4.7.51"` in the server configuration. The server applies missing rows in memory only; omit the setting for an unknown source. Existing caches and player saves are not rewritten.
- The output must be a new file that does not exist yet; after writing, the tool reads it back and checks the row count of every collection.
- `fill-report.txt` lists the load warnings, the export row count / old cache row count / filled row count of every member, and the number of main missions whose start or end sub-missions are not registered, before and after filling.

## 4. Verify, replace and roll back

```powershell
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj -- --cache artifacts/<name>/Resource.new.bin
dotnet run --project scripts/LoginSceneRegression/LoginSceneRegression.csproj -c Release -- --resource-cache artifacts/<name>/Resource.new.bin
dotnet run --project scripts/DailyActiveRegression/DailyActiveRegression.csproj -c Release -- --cache artifacts/<name>/Resource.new.bin
```

For a cache built from a verified 4.7.51 export, append `--resource-version 4.7.51` to the LoginSceneRegression arguments to test the Star Lotto runtime overlay. Run `dotnet run --project scripts/ResourceCacheFill/ResourceCacheFill.csproj -c Release -- --self-test` for the compatibility checks without a cache.

After the regressions pass, back up the existing `Config/Resource.bin`, then copy the new file over it. To roll back, copy the backup back; for a version in which enum values changed, restore the program and the cache together.

## Results for OSBETAWin4.7.51 (2026-11-14)

- Export: the index has 144 867 resources; the built-in list named 130 581, and 134 450 after name supplementation converged; 133 541 JSON files were written, 710 of them floors; 10 417 resources still have no path.
- 22 tables could not be decoded: 21 `MechCraft*` tables and `PetConfig` (the dumper reports `UnknownFieldType`; the row type cannot be identified); `DecideAvatarOrder` is an empty table in the client. The maze skill files of 10 new characters, such as `LocalPlayer_Pearl_00`, `Nihilux_10`, `Ellen_00` and `Astra_00`, are not in the index.
- Loader changes: `GridFightBonusTypeEnum.AllAvatarByRarity = 25` and `GridFightTraitEffectTypeEnum.GainOtherRoleTrait = 34` are new members used by the new tables, with values matching the client; no other members changed.
- Filling: 3 113 rows in 55 members were filled from the 4.6.52 cache (MechCraft, pets, rewards, sub-missions, 57 floors and more). The first version of the cache also filled 4 191 rows of the four message tables (156 message groups, 156 sections, 3 873 messages, 6 contacts). Of these, 6 groups under 4.7.51 contacts (15046, 15047, 20189, 20190, 20238, 20239) were sent to the client as completed messages; the client did not recognize their sections, and handling of the whole message list failed. The cache was regenerated with these tables taken only from the export. Main missions whose start or end sub-missions are not registered went down from 179 to 18: 1054800–1054804, 3050321, 4010263–4010265, 4011900, 5000514, 8048117–8048121, 8048208, 8048209.
- Floor paths: the initial list did not derive floor paths from planes, so the floor `P30148_F30148002` of the latest Pure Fiction period `20294` was not exported, and `LoginSceneRegression` failed with "PF 20294 starts on stage 1". After adding such candidates, 13 more floors and the groups they reference were exported, and that check passed.
- GridFight: the new tables change Aha's character ID from `151102` to `151112` / `151122` / `151132` and adjust the investment list and other content; for the server's adaptation to the second season, see [GridFight comparison and regression](../testing/grid-fight-regression.md).
- Regressions on the new cache: GridFightRegression 557/557, LoginSceneRegression 717/0, DailyActiveRegression 76/0.
- After adding `RealityShowBuffNum` / `RealityShowResult` / `RealityShowAllResult` (470011–470013), the cache was regenerated from the same export directory and the 4.6.52 base cache (`Resource.4.7.51.realityshow.bin`). The filling result is unchanged (3 113 rows in 55 members, main missions 179 → 18). FinishWay rows of type `Unknown` went down from 712 to 650, and mission rows of activities such as Road to Fame and Containment Starts With a Roll now have the correct type. The Containment Starts With a Roll missions therefore really advance during the stage flow, and the corresponding handler check in `LoginSceneRegression` now uses a new account. On the new cache: GridFightRegression 557/557, DailyActiveRegression 76/0; LoginSceneRegression has only one failure left, TrainParty, which was under parallel development (the old cache fails it as well). The backup of the cache before replacement is `Resource.bin.backup-20261114-before-realityshow.bin`.
- `ExcelOutputGameCore/AvatarSkin.json` and `ExcelOutput/ItemConfigAvatarSkin.json` in this beta archive both decode in full to eight rows and omit outfit `1151101`. The configured CN and OS 4.7.51 download manifests and indexes match the local copies byte for byte. Aeon ★ Aha's Star Lotto (`1151101`, avatar `1511`) is present in the 4.7 release tables. With the 4.7.51 runtime overlay enabled, the server fills in missing Star Lotto rows when Aha is present so `/giveall skin` can grant it. The client needs the corresponding resources to display the outfit. [Outfit announcement](https://www.hoyolab.com/article/47252512), [release outfit table](https://github.com/DimbreathBot/TurnBasedGameData/blob/main/ExcelOutput/AvatarSkin.json), [release item table](https://github.com/DimbreathBot/TurnBasedGameData/blob/main/ExcelOutput/ItemConfigAvatarSkin.json).
- Generate the cache with code of the same version as the program: after the scene, mission and other loaders gain fields, those fields hold default values in a cache generated by an old program. The cache, reports and the replaced 4.6.52 cache from this run remain under `artifacts/resources-4.7.51/` on the machine that generated them and are not distributed with the repository. None of this has been verified on a real client.
