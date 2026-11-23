# Proto

English | [简体中文](protobuf.zh-CN.md)

This document describes how the server's protocol definitions are stored, built and migrated to a new client version. Read it before renaming protocol types or fields, regenerating command IDs or switching the active protocol version.

Source: [`Proto/`](../../Proto/)

Protobuf definitions are kept per game version and grouped by `Cmd*Type`. The active version is **4.7**: `ActiveProtocolVersion` in [`Proto/Proto.csproj`](../../Proto/Proto.csproj) selects it, and at build time the project generates C# from the top-level `*.proto` files of `Proto/4.7/` into `obj/<configuration>/<framework>/protobuf/4.7/` and compiles that. The generated C# is not under version control.

This branch keeps the 4.7 protocol so that the server works with the retail OSPRODWin4.7 client. Upstream `main` has moved to the 4.8 protocol in a single `Proto/StarRail.proto`; its layout, its ProtoOrganizer commands (`carry`, `cmdids`) and its upgrade steps do not apply here.

## Layout

```text
Proto/
  <version>/                   one directory per game version
    Cmd<Xxx>Type.proto         definitions grouped by command module
    Common.proto               types shared across modules
    Orphans*.proto             types not yet assigned to a command closure
    Ungrouped.proto            types reached only from command IDs that no Cmd*Type enum lists
    CmdIds.cs                  command ID snapshot of the version
    Raw/                       flat master StarRail.proto, packetIds.txt and translations.txt
    dumper/Raw/                own raw dumper output, never mixed with reference material
    reference/                 optional external reference, kept only when one really exists for that version
    compat-types.txt           API types kept from the previous version during bring-up
    cmdid-aliases.txt          compatibility aliases from old server command names to current command ID names
  4.5v3/translations.txt       same-seed input of LoginSceneRegression, tracked outside the archives
  Archives/                    lossless ZIP archives of older protocol versions, extracted on demand for a migration
  Build/                       MSBuild targets for automatic generation
  obj/.../protobuf/4.7/        active C#, generated at build time and ignored by Git
  Tool/ProtoOrganizer/         tool for version migration, grouping, command IDs and C# generation
```

Only the top-level `*.proto` files of the active version are compiled. `Raw/`, `dumper/`, the text lists and the other version directories are ProtoOrganizer inputs and never part of the build. The files of `Proto/4.7/` are described in the section on the active 4.7 version below.

Older versions used other layouts and are archived. 4.4, the compatibility baseline from before this layout, keeps its grouped definitions and `Raw/` at the version root, with its own dumper output in `4.4/dumper/Raw/`; 4.5 and 4.5v3 keep their grouped output under `dumper/`. No reference was invented for 4.5: until a real 4.5 reference is available, only the dumper output, the alignment with 4.4 (same obfuscation seed) and the explicit compatibility lists are used.

Resource extraction and Honey's resource cache are separate: Mar7thDumper still runs its full pipeline, including the raw resource export, while ProtoOrganizer only consumes the proto and command ID output. It never writes `Config/Resource.bin` or switches Honey's resource directory.

## Archives and the same-seed input

The inactive reference files of 4.4, 4.5, 4.5v3 and 4.6 are kept as lossless ZIP archives in `Proto/Archives/`. A normal 4.7 build does not need them. Before running a command that reads an old version's paths, such as the example below, extract the versions it needs as described in the [archive notes](../../Proto/Archives/README.md); the extracted directories are ignored by Git.

`Proto/4.5v3/translations.txt` stays tracked as a plain file outside the archive because `LoginSceneRegression` reads it directly. Its same-seed check passes the forced mappings of this file to ProtoOrganizer's `SameSeedMigrator` (the regression references the ProtoOrganizer project for this) and expects the explicit 4.5 field corrections of `SceneMapInfo` (`floor_id = 14`, `retcode = 6`) to survive a full same-seed regeneration, not only protoc. Do not archive or delete the file while that check exists.

## Historical 4.5 migration example (not the active version of this branch)

First extract 4.4 and 4.5 as described in the [archive notes](../../Proto/Archives/README.md). The C# written to `Proto/Generated` only serves to check the tool; the server build never compiles that old path. The command also writes `KcpSharp/CmdIds.cs`, so do not run it on this branch to replace the current command IDs or the active version.

```sh
dotnet run --project Proto/Tool/ProtoOrganizer/ProtoOrganizer.csproj -- \
  --proto-file Proto/4.5/dumper/Raw \
  --raw Proto/4.5/dumper/Raw \
  --out Proto/4.5/dumper \
  --same-seed-raw Proto/4.4/dumper/Raw \
  --same-seed-proto Proto/4.4 \
  --translation Proto/4.5/translations.txt \
  --compat-types Proto/4.5/compat-types.txt \
  --cmdid-aliases Proto/4.5/cmdid-aliases.txt \
  --version 4.5 --active --compile \
  --cs-out Proto/Generated \
  --cmdids-out Proto/4.5/dumper/CmdIds.cs \
  --cmdids-out KcpSharp/CmdIds.cs
```

The tool reads the dumper's raw definitions and `Cmd*Type` enums, recovers the command IDs, aligns types and fields with the older version that has the same obfuscation seed, adds the versioned compatibility types and command aliases, regroups the definitions and runs `protoc`. A version is used for the Honey build only once all of its protos compile.

When existing grouped protos only need semantic names, `--rewrite-proto` names the files to translate in place and `--compile-rewrite` the files whose C# of the same name is regenerated. This mode applies only the type mappings of the translation table (`--translation`) that start with `!` and brings in no other definitions from Raw; `--cmdid-aliases` together with `--cmdids-out` can update the semantic command aliases at the same time.

ProtoOrganizer finds `protoc` for the current system in the `Google.Protobuf.Tools` NuGet cache; `--protoc <path>` overrides it.

## Build and verification

```sh
dotnet restore March7thHoney.sln
dotnet build March7thHoney.sln -c Release --no-restore
python3 scripts/test-proto-generation.py
```

NuGet pins `Google.Protobuf.Tools 3.33.1` (protoc 33.1), matching the existing `Google.Protobuf 3.33.1` runtime; both versions are declared in `Directory.Packages.props` at the repository root. The first build needs to restore this tool package; the tool does not go into the server's published output. On Linux and Windows the host platform's tool is used, and cross-publishing does not change that choice. The macOS tool in this version is x64, so Apple Silicon needs Rosetta; alternatively `-p:ProtocolCompiler=<path>` can point to a protoc of exactly version 33.1.

The build target detects added, removed and modified schemas and missing generated files; when nothing has changed, generation is skipped. Building again after `dotnet clean` restores all generated code. Change the grouped definitions; do not edit `obj/` by hand.

Alongside the generated code the target writes `ProtocolDescriptors.g.cs`, whose `ProtocolDescriptors.BuildAll()` lists the `<File>Reflection.Descriptor` of every compiled schema (each schema keeps `option csharp_namespace = "March7thHoney.Proto"`) so that the server builds all descriptors at startup without reflection, which NativeAOT trimming could silently defeat.

`scripts/test-proto-generation.py` runs the generation in a temporary directory whose path contains spaces, on copies of `Proto.csproj`, `Build/` and the active version's schemas next to the repository's `global.json`, `Directory.Build.props` and `Directory.Packages.props`. It checks fresh and incremental generation, the repair of a missing output, the addition, modification and removal of a schema, and that a target runtime does not change the host compiler.

The checkout is much smaller before a build, but the generated code and the NuGet cache still take space at build time. Old Git history is not rewritten; a full clone may still download large historical files.

## Command IDs

`KcpSharp/CmdIds.cs` holds the command IDs of the active version. ProtoOrganizer generates it together with the version's snapshot `Proto/<version>/CmdIds.cs` (the two `--cmdids-out` arguments of the commands in this document): one constant per command ID, named after its `Cmd*Type` enum member or its readable name in `Raw/packetIds.txt`, followed by the aliases from `cmdid-aliases.txt`. An alias names an existing constant, not a number.

`scripts/verify-security.py` checks that `KcpSharp/CmdIds.cs` is byte-identical to `Proto/<ActiveProtocolVersion>/CmdIds.cs`. ProtoOrganizer has no compare-only mode, so regenerate both copies together and never edit one of them by hand.

A command enum name is not necessarily the protobuf message type name: handlers bind to the current command constant and parse the real message type, so searching for a `message` with the same name as the enum member can miss the existing structure. When a request has no handler, the framework automatically sends an empty success response; that response cannot be taken as evidence that the feature ran.

## The active 4.7 version

The raw 4.7 definitions come from a static dump (`demiurge proto`) of the client's `GameAssembly.dll` and `global-metadata.dat` made with [DemiurgeCSharp](https://github.com/Mar7thDev/DemiurgeCSharp); a runtime dumper is no longer needed.

```text
Proto/4.7/
  dumper/Raw/StarRail.proto    raw Demiurge output: obfuscated type names (recovered real names carry // obf:), 4.7 field numbers and cmdIds
  Raw/StarRail.proto           translated flat master the grouped files are generated from
  Raw/packetIds.txt            cmdid -> name (// obf: original obfuscated name)
  Raw/translations.txt         obfuscated type -> name
  overrides-manual.txt         4.7 type names checked one by one, each with its evidence
  migration-report.txt         record of the 4.6 -> 4.7 migration: additions, removals, disagreements between evidence sources
  cmdid-aliases.txt            the 4.6 aliases, followed through cmdid -> obfuscated name to the 4.7 names
```

The migration relies on obfuscated identities being stable between versions: a type's obfuscated name, its property names and its `XxxFieldNumber` constant names are the same in every version; only field numbers, cmdIds and backing field names change. Demiurge therefore names fields after their properties, and the type, field and enum member names reviewed for 4.6 were carried one by one, by obfuscated identity, onto the 4.7 field numbers. Field identity is matched by decreasing strength of evidence: fields pinned by hand in 4.6 and obfuscated names kept as they are → the 4.4/4.5 Mar7thDumper constant names matched by field number to the readable definitions of the same version → the same field number in the 4.6 master → the automatic 4.6 overrides → the 4.6 dumper raw output (original name or `nt.txt` translation). `nt.txt` comes from matching storage slots, so adjacent fields of the same type can be swapped (SceneInfo's `world_id` / `leader_entity_id` and `lighten_section_list` / `opened_chests_list` are such cases); it only identifies a field and never decides a name on its own.

To maintain the 4.7 definitions, edit `Raw/StarRail.proto`, then rebuild the grouped definitions and the command IDs with the command below. `--cs-out Proto/Generated` is an optional output for checking the tool; it is ignored by Git and not compiled into the server. Afterwards build normally so that MSBuild generates the C# from the active grouped definitions into `obj/`:

```sh
dotnet run --project Proto/Tool/ProtoOrganizer/ProtoOrganizer.csproj -- \
  --proto-file Proto/4.7/Raw --raw Proto/4.7/Raw --out Proto/4.7 \
  --version 4.7 --active --compile --cs-out Proto/Generated \
  --cmdid-aliases Proto/4.7/cmdid-aliases.txt --compat-types Proto/4.7/compat-types.txt \
  --cmdids-out Proto/4.7/CmdIds.cs --cmdids-out KcpSharp/CmdIds.cs
```

`LoginSceneRegression` contains version-specific cmdId and field number assertions (VideoKeyInfo, SceneMapInfo, SyncServerSceneChangeNotify, the CommonRogueQuery branch, quick level-up, common lineups, classic Simulated Universe 18xx commands); update them when switching the active version.

Quick level-up is an example of a command enum name differing from the message type name: in 4.6, `KELLLHMHFHL` (376) corresponds to `EKLIDIDKFHC` in `dumper/Raw/cmdid-registry.txt`, and the captured response is `PMEMIDKDAFG` (357, enum `MILFFHPMLMN`); in 4.7, `Raw/packetIds.txt` and the definitions with the same identities give 355/341. The 4.6 official server capture seq `36813-36829` can verify the request/response pairing and the level-up cost; its old command IDs cannot be used directly for 4.7.

The Light Cone quick level-up identity is `EOBJKAMNNPI → AMNBMKEMMFL`. The 4.6 raw definitions mislabelled it as a duplicate `PromoteEquipmentCsReq/ScRsp`; the raw messages of official server seq `36852/36859/36927` decode to the target level, unique ID, EXP cost, ascension cost and ascension level respectively. 4.7 maps it to `EquipmentQuickUpCsReq/ScRsp` (513/517): `IGOOJKMPMDC` is the target level, `KLMIBJIKIBB` the target ascension, `BOBOKJBAMNB` the EXP materials or fodder Light Cones, `IDGEEENIMMK` the ascension materials. Use the existing active definitions by property identity; do not change it into `PromoteEquipmentCsReq`, which only performs ascension.

## Code excluded from the build

`GameServer/GameServer.csproj` removes some sources from the build with explicit `<Compile Remove>` items, each with a comment that gives the reason:

- The DiceCombat handler `HandlerDiceCombatFinishPveStageCsReq`: 4.6 left only two `uint32` candidates of the same type, and the old mapping was an unverified guess, so it is not guessed again.
- The 4.8-only activities merged from upstream, currently ActivityBangboo, DiceyDungeon and RealityShow: their game logic, request handlers, send packets and mission finish handlers. Their commands and messages are missing from the 4.7 protocol, or the 4.7 messages of the same name carry different fields, and the mapping is not guessed. [ActivityBangboo](activity-bangboo.md), [DiceyDungeon](dicey-dungeon.md) and [RealityShow](reality-show.md) describe their 4.8 protocol. While they are excluded, their player components are not wired up and battles carry no RealityShow context.

`LoginSceneRegression` leaves the matching cases out as well: `<Compile Remove>` in its project file for the activity regressions, and `#if PROTOCOL_48_ACTIVITIES` around the inline cases. Before re-enabling an excluded file, resolve its protocol evidence and compatibility problems against the active version, and re-enable its regression cases with it; do not simply delete the exclusion to make it compile again. Never copy 4.8 command IDs or field numbers into 4.7.

## Constraints for a new version

- Own dumper output always goes into the version's `dumper/Raw/` and is never mixed into a reference.
- `reference/` holds only reference definitions of the same game version. While none is available, leave it absent or keep the old version's directory; never fake a reference for a new version.
- Compatibility types and command aliases go into the lists of the version they belong to; never write an old version's cmdId numbers over a new version.
- Never decide a mapping from fields having the same type alone.
- When switching the active version, update `ActiveProtocolVersion` in `Proto.csproj`, the grouped definitions and `KcpSharp/CmdIds.cs` together, then run `dotnet build March7thHoney.sln` and the generation regression. Also update the version-specific assertions in `LoginSceneRegression` and check the `<Compile Remove>` exclusions in `GameServer.csproj` against the new protocol.
