# Raw resource loading regression

English | [简体中文](resource-regression.zh-CN.md)

`ResourceRegression` loads a raw resource directory with the production loader in an isolated process and can export a verified resource cache. Run it after changing resource models, enums read from resource tables, or how shared abilities are resolved, and whenever you build a new `Resource.bin` from raw resources.

Project: [`scripts/ResourceRegression/`](../../scripts/ResourceRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources directory> <new report directory> --cache-out <new cache file>
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources directory> <new report directory> --stale-cache <pre-adaptation cache> --cache-out <new cache file>
```

- `--cache-out` is optional. When given, it must name a file that does not exist yet. The regression first does a MemoryPack round trip, then writes a compressed snapshot with CreateNew, and only if every check passed.
- `--stale-cache` copies an old cache into the report directory, checks that a cache missing the adaptation results is recognised when the new raw tables are present, and then reloads the resources. It never replaces the production cache.

To run the GridFight regression against the new cache, pass it as a read-only source; the program copies it into its own test directory first:

```powershell
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj -- --cache <new cache file>
```

## Prerequisites

- A raw resource directory (`Resources`) of the client version you are targeting.
- The regression calls the production `ResourceManager.LoadGameData()` directly. It starts no server, database or player object, does not read the production `Config.json` and does not replace the existing cache. Operator custom configuration and language files are loaded read-only from the repository's `Config`.
- Before deploying a new cache, back up the program and the existing `Config/Resource.bin` and confirm the resource path, then switch to the new program and the new cache together. The loader prefers the cache; changing only ResourcePath does not replace an existing cache.
- If you have only a client and no complete raw resources, follow [Rebuilding the resource cache from a client export](../operations/resource-cache-from-client-export.md).

## What it covers

### Loader behaviour

- A missing optional table still loads; a missing required table still warns and prevents a complete cache; a parse error in an optional table that does exist still fails.
- The report directory holds the load logs, `audit.json` and the isolated test fixtures. The fixtures deliberately trigger missing files and parse errors.
- ResMgr/ResCache log per-file resource misses and loader diagnostics at debug level, which reaches only the debug log, so the regression reads them back from `loader-debug.log` in the report directory and counts them in `loader issues` and in the `Warnings` of `audit.json`. Their operator-facing warnings arrive through `OnLogEntry` like any other warning.
- The final production load must produce no warning or error.
- The full production load and the snapshot round trip.

### Optional tables

The OSBETAWin4.7.51 client index does not contain DialogueDynamicContent, DialogueEvent, GridFightGenderOverride, ItemConfigTrainDynamic, RogueDLCBossBp, RogueWolfGunMiracleTarget or TrainPartyDynamicConfig. They are historical or dynamic companion tables with no current reader, or override tables that already fall back to the base gender, so each file is declared optional. Required tables such as the main ItemConfig are not affected. Files the client does contain are still parsed in full; a table that does not exist must not be filled with empty JSON.

### Shared abilities

NPC variants reference common, prototype-monster or FakePlayer configurations, so an entity's file name does not necessarily match its ability file name. Shared abilities are resolved from the character's AbilityList and SkillList.EntryAbility; conflicting or genuinely missing definitions are still errors and are never treated as an empty configuration. WhiteBox development records that come without a character file do not take part in ability loading; these test records are not part of the client's release scenes.

### GridFight tables

- The new GridFight enum values.
- GridFightCampSeason's MonsterList is merged with the base camp table. The current season is looked up by camp ID; other seasons are kept under an internal composite key; the IDs sent to the client do not change.
- The role table has no duplicate IDs: season 2 uses six-digit role IDs, and AvatarID is still the base character ID.
- The new layout no longer requires RoleSavedValueList; skills are resolved from the original GridFightRoleStar table, and the cache records this layout flag.
- The season 2 healing talent is 3011; formation selection ignores the default row with ID 0.
- Old combined tables keep their original loading path.
- Random role rewards are filtered by season; roles from another season are rejected.
- `IsFakeAvatar` records are kept in the cache. Pom-Pom 604202 has no ordinary AvatarConfig and is not added to the ordinary role pool.

## Expected result

The run ends with `Resource regression checks: <n>, failures: 0, loader issues: 0` and exits with code 0.

## Known gaps

- **GridFight enum values do not match the client.** The current code uses server-side placeholder values for the high members of `GridFightBonusTypeEnum`, `GridFightTraitEffectTypeEnum` and `GridFightConsumeTypeEnum` and for `GridFightEquipFuncTypeEnum.AddTraitLayer`, and existing `Config/Resource.bin` files were generated with those placeholders. The check "beta bonus enum values match the client" therefore fails and `--cache-out` writes no cache. The OSBETAWin4.7.51 client values are:
  - BonusType: SpecificAvatarWithEquip 16, SpecificAvatarWithRandomEquip 17, SpotLightReplaceCnt 18, Augment 19, SpotLightFreeRefreshCnt 20, TrailblazeShopCoin 21, ElationGoods 22, GlobalHp 23, ElationItem 24, AllAvatarByRarity 25.
  - TraitEffectType: BandRoleChoose 19, MoonGame 24, MoonGameSpotLight 25, DanHengTwoInOne 28, JadeShop 29, ClaraEnhance 31, AdditionalTraitRoleChoose 32, AhaUnique 33, GainOtherRoleTrait 34.
  - ConsumeType: UpgradeChimera 7, CopyChimera 8.
  - EquipFuncType: AddTraitLayer 12.

  The four OSBETAWin4.6.51 GridFight enum groups were likewise taken from the explicit enum values in the same DLL (SpecificAvatarWithEquip 16, SpecificAvatarWithRandomEquip 17). These enums are only read from tables by name and never go on the wire, but switching to the client values changes the meaning of the integers stored in the cache, so the change must ship together with a regenerated cache. After updating the program, generate a new cache from resources of the same version; to roll back, restore the matching program and cache together.
- The NPC-specific battle logic for Pom-Pom 604202 is not implemented.
- Read-only loading and cache verification do not replace quest or battle verification on a real client.

## 4.7.0 retail client read-only probe

```powershell
dotnet run --project scripts/RetailResourceProbe -c Release -- <game install directory> TurnBasedGameData <new output directory>
```

The probe reads the DesignV indexes of `StreamingAssets` and `Persistent`, verifies the content digests of the indexes and of the data blocks it uses, and prefers the hot-update entry when both have the same key. It supports only two GridFight tables and the selected `MissionInfo` records; it is not a general unpacker. The output directory must not be inside the input directory and must not exist yet.

The output contains numeric-enum JSON for the two tables, the `MissionInfo` of `3050321` and `4011900`, and a `report.json` with the block digests, offsets and comparison results. Enum names come from reference data matched by row ID, not from strings in the client. The two neighbouring missions serve as a decoding cross-check; the main mission record does not contain the separate sub-missions or behaviour graphs. The files are for checking only; do not copy them over the server resources. Exit code `0` means the probe passed, `2` a comparison mismatch, and `1` an input or control check failure.
