# Containment Starts With a Roll (DiceyDungeon)

English | [简体中文](dicey-dungeon.zh-CN.md)

> **Not built on this branch.** DiceyDungeon is excluded from the build (`<Compile Remove>` in [`GameServer/GameServer.csproj`](../../GameServer/GameServer.csproj)) because its commands and messages are missing from, or differ in, the active 4.7 protocol. Its `Config/Custom/DiceyDungeon.json` is still shipped and loaded.

This document describes the server side of the DiceyDungeon event added in 4.8: its unnamed protocol, where its configuration comes from, the rules the server enforces, and how the client reaches the event's entrance NPC. Read it before changing the event, re-exporting its configuration, or debugging an entrance button that does nothing.

## Overview

The event "Containment Starts With a Roll" was added in 4.8 (client OSBETAWin4.7.51). Client module `RPG.Client.DiceyDungeon`, activity `50140`, panel `50140`, activity module `5014001`. The dice battles run entirely on the client; the server keeps the progress of a run: which level and node are being played, the character, HP, equipped skills and curios, nodes passed, and the reward choice after each win.

| Content | Path |
| --- | --- |
| Gameplay logic (player component) | `GameServer/Game/Activity/DiceyDungeon/DiceyDungeonManager.cs` |
| Request handlers | `GameServer/Server/Packet/Recv/DiceyDungeon/HandlerDiceyDungeon.cs` |
| Responses | `GameServer/Server/Packet/Send/DiceyDungeon/PacketDiceyDungeon.cs` |
| Save table `PlayerDiceyDungeon` | `Common/Database/DiceyDungeon/PlayerDiceyDungeonData.cs` |
| Configuration | `Config/Custom/DiceyDungeon.json`, model `Common/Data/Custom/DiceyDungeonConfig.cs` |
| Event missions | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerDiceyDungeonFinish.cs` |
| Schedule | `5014001` / panel `50140` in `Config/Custom/ActivityConfig.json` |
| Regression | The DiceyDungeon section of `scripts/LoginSceneRegression` |

## Protocol

The command directory `LDJDDDDPLLH` (10643–10659), new in the 4.8 dump, is unnamed as a whole. Messages and fields keep their obfuscated names and were not written into the 4.8 master. The meanings come from the client code: requests are sent by the network service class `LCBFIHEMOOC`, whose async state machines keep the real method names (`GetActivityDataAsync`, `EnterNodeAsync`, `FinishNodeAsync`, `_SendSelectRewardAsync`, `RerollRewardAsync`); responses are read by the data models `FLIFOAOEFPP` / `MJHOIBEANAJ`, and the latter's auto-properties keep real names such as `<LevelID>k__BackingField`. The field mapping was obtained by disassembling the request construction and response synchronisation code and comparing storage offsets. The obfuscator derives names from the original identifiers, so the same obfuscated name means the same thing in different classes (for example `JKCOAIGJMMM` is the retcode in every response).

| cmdId | Message | Direction | Fields |
| --- | --- | --- | --- |
| 10646 | `DLDDOMMLPBK` | Request: get data | none |
| 10659 | `OBJJEHIKFJC` | Response | `FJCAKOIDONG` data (`LCIKABPABHK`), `JKCOAIGJMMM` retcode |
| 10658 | `NOJMGKOMJHP` | Request: enter node | `IJAPHCOHMIN` level, `CDMFHNGEFCN` node, `ILHPFNCIOJN` character, `JHLGHLBDMLH` skills, `KIIIDKADAIN` curios |
| 10652 | `OHCCEANPCIL` | Response | level, node, `NBNKLEBNPFA` run data, retcode |
| 10654 | `PIEGCFMAEOO` | Request: settle node | level, node, `EKFDEEHCKDH` won, `GNGBGCHGJPD` remaining HP, `BOILBFJANKL` enemy remaining HP, `BJKGNCLCKBG` turns, `LALKFADHJJL` time taken |
| 10655 | `NMNFJDBNBPG` | Response | level, node, won, `NIMINCMIMEE` level settled, `BDCOFIOCJFO` stars, `CCBNBBGBKCE` furthest node (0 means no update), run data, retcode |
| 10657 | `KPNDLNCAECC` | Request: choose reward | oneof: `ALPKKNKAFBG`(9) skill `{GHMHMKINNEJ skill, KFGFGMFAMDA skill replaced}`, `ANBKDJKHCBI`(4) curio `{LJCEOGPOGBE}`, `POJDOCAKCJM`(11) give up, `NBNMHLIFCMF`(8) not seen used by the client, handled as give up |
| 10656 | `GOPAMMODIJI` | Response | retcode, run data |
| 10645 | `IMPHFFIJDKP` | Request: reroll reward | oneof: `ALPKKNKAFBG`(13) skill, `ANBKDJKHCBI`(2) curio |
| 10651 | `HJLMNPJFEMI` | Response | oneof: `DPLODKBIJDI`(6) skill choice, `CACADJOIHHI`(14) curio choice; retcode |
| 10648 | `MDOHKDNNDPI` | Request | No fields. No client C# code constructs it; it presumably comes from Lua, and its purpose is unknown |
| 10643 | `LHDOHPHPABA` | Response | retcode only; the server only answers success and does not change progress |

Run data `HBBPNKMJFBF`: `IJAPHCOHMIN` level, `ILHPFNCIOJN` character, `JCLIFKACIJD` battle serial number (SerialNumber), `FHHOPLLDPAP` current node, `OMIIAOCIIPA` current HP, `JHLGHLBDMLH` skills, `KIIIDKADAIN` curios, `BAFLDOEHBJK` nodes passed, `DPLODKBIJDI` pending skill choice, `CACADJOIHHI` pending curio choice. Pending reward `MHLHDALAFMJ`: `GGBGEJIIKFN` candidates, `MNBMPEIABNJ` HP restored on giving up, `LBDOBLGKANH` rerolls left. Level record `OGDOODOGIOF`: `IJAPHCOHMIN` level, `HCOFMFKFMDK` finished, `BDCOFIOCJFO` stars, `GKALFLHFPKC` furthest node.

The client state is derived entirely from the responses: with a pending reward (non-empty candidate list, skill before curio) it enters PendingReward, otherwise PendingChallengeNode; when `NIMINCMIMEE` in the settle response is true it enters LevelSettling and ignores the run data.

## Configuration source

Levels, nodes, characters, monsters, skill pools, curio pools and constants are all in client ExcelOutput tables, but the paths of these tables are not in the community name list, so the resource export does not produce them, and the table class and row type names are obfuscated. `Config/Custom/DiceyDungeon.json` was produced by trial-decoding the unnamed resources in DesignData one by one with Demiurge by row type, then cross-checking with level and node IDs; `_source` in the file records the resource hash of each table:

| Content | Row type | Resource hash |
| --- | --- | --- |
| Levels (5 rows, 1–4 story, 5 challenge) | `JCDIPAECJHB` | `6ab08a7d3f739d62` |
| Nodes (29 rows, chained by `PreNodeID`) | `ICPBAAIKFFE` | `c1643faeeb64c1d6` |
| Monster HP | `BGCDHJCDNIA` | `2f85659fbe0aa4ca` |
| Characters (initial HP 1000, story / challenge skills) | `FCCIAHCGLDN` | `df826799821d351a` |
| Skill pool / curio pool | `CJPDKEFCJNI` / `OBBGJBLAJKE` | `427780f4afd6d5c4` / `215e3f5962540f7e` |
| Constants (at most 5 skills, at most 3 curios in the challenge, 1 reroll, 100 HP restored on giving up, star thresholds 80/60/20) | `GHPMCNKGPMF` | `4288f0425c3c43d9` |

The file is not part of `Resource.bin`; it is read from `Config/Custom` on every start. After a client update it has to be exported again the same way.

## Rules

- Entering the first node of a level starts a new run, and a run in progress on another level is discarded. Story levels use the character assigned by the level with its initial story skills; challenge levels accept only the characters the level lists, and skills and curios must come from that character's challenge skills / curios and stay within the limits. Entry is refused while the prerequisite level is unfinished.
- Only "the current node (not passed)" or "the node after the current node, once it is passed" can be entered; the run cannot advance while a reward choice is pending. Each entry takes a new battle serial number.
- Story level lost: the run stays on the same node and can retry. Won: the node and the remaining HP (capped at the initial HP) are recorded. A win on the last node finishes the level and clears the run; otherwise a reward choice is offered: one skill choice per win, plus one curio choice on elite nodes. When the node configures fixed candidates they are used; otherwise 3 not-yet-owned entries are drawn by weight from the character's pool.
- Only after node 103 has been passed can a reward be rerolled (once) or given up for HP (+100, capped at the initial HP).
- A challenge level has a single node and every settlement ends the run: a win gives 3 stars, otherwise stars follow the damage dealt as a share of the boss's HP (≥80% 3 stars, ≥60% 2 stars, ≥20% 1 star). The best result is kept, and 1 star or more counts as finished.
- Event missions: `DiceyDungeonNodeFinish` (480600) / `DiceyDungeonLevelFinish` (480601) count by the node / level IDs in `ParamIntList`.

## Entrance

The event has no entrance screen of its own: "Go" on panel 50140 is just a jump, and the mode is opened from an entrance NPC in the scene. The related configuration all sits on content package `150137` (`ContentPackageConfig`): guide mission `8048208`, hidden standing mission `8048209`, guide entrance `2058104`, standing entrance `2058105` (scene `20581001`, group 159, anchor 1).

The client's decision chain (`CommonActivityGotoBtnBehaviour.on_btn_go`, with the state coming from `ActivityStateData._RefreshState_Internal`):

1. While the `GuideConditions` of `ActivityPanelCondition` (`FinishMainMission:8048208`) are not met, the button takes the guide jump `6428` (`MultiMainMission`); once they are met it takes the activity jump `6427` (`AreaSwitch [2058105, 2589, 150137]`).
2. The activity jump looks up map icon `2589` (`MappingInfo` type `ACTIVITY_ENTRANCE`) in the target scene's map data; the icon is attached to NPC `3315` in group 159. If found, the map opens and centres on it.
3. Group 159 is a client group whose `OwnerMainMissionID` is `8048209`. Officially this mission stays in progress for the whole event, which is what makes the group exist.
4. After entering the scene, the client walks the unfinished sub-missions and activates the groups listed in each sub-mission's `GroupIDList` (`MapDef.InitActivateHoyoGroupByMission`, which requires the sub-mission's `LevelFloorID` to be the current scene). `8048209` has two steps (client `MissionInfo_8048209.partial.json`; this resource is not in the export list and was resolved by its path hash): `804820901` (`Auto`, `SubMissionFinishCnt [804820801]`, waits for the first step of the guide, empty `GroupIDList`) and `804820902` (`AnySequence [804820901]`, `Talk`, `GroupIDList [159]`). So `804820902` itself must be in progress for the entrance NPC to spawn; reporting only the main mission as unfinished, or reporting `804820901` as in progress, is not enough. Earlier events have the same structure (`8043204` of content package `150077`: `804320401` / `804320402`, the latter carrying entrance group 182).

The server does three things for this. Without either of the first two the button "does nothing when clicked", and the client reports no error; without the third the player is teleported to the spot but no NPC is there:

- **The guide mission's finished state stays consistent.** The client writes the finished / unfinished main mission lists from the scene mission info (`MissionStatusBySceneInfo`) into `MainMissionData.IsFinish`, and the `FinishMainMission` condition reads that flag. `BuildSceneMissionInfo` adds the content package's missions to the "unfinished" list so that event entrances stay visible; if the guide mission were added that way too, the button would fall back to the guide jump once the player had entered that scene, and the guide mission has nothing left to do. Therefore every main mission that `GetMissionStatusScRsp` has already reported as finished in this session stays reported as finished in the scene mission info (`MissionManager.NoteMainMissionsReportedFinished`); missions the client has not asked about (the standing mission `8048209`) stay unfinished.
- **The standing entrance group gets into the map data.** `MissionManager.IsGroupOwnerMissionActive` replaces the old "the owning main mission must be in progress" check: the start group of a content package's standing entrance (`ContentPackageConfigExcel.TryGetStandingEntrance`: there is an activity module, the guide entrance differs from the standing entrance, and the standing entrance's start group is owned by the content package's own mission) also counts as active when its owning mission reads as "finished". With the mission system off, every mission reads as finished; with it on, a standing mission disabled because its sub-mission data is missing reads the same way. While the mission has not been reached (`None`) or the event is not on the schedule, the group still does not appear.
- **The standing mission stays on its last step.** For such an "open standing entrance", the scene mission info puts the standing mission in the unfinished list, sends its `FinishSubMissionList` (the last step, `804820902`) as `MissionDoing` and its `StartSubMissionList` (`804820901`) as `MissionFinish` (`MissionManager.AddStandingEntranceMissions`); `GetMissionStatusScRsp` gives the same answer for the same main missions / sub-missions and does not report the standing mission as finished. The server resources have no definitions for these two sub-missions, only the main mission's start / finish sub-mission lists, so the finish list is treated as the step to stay on; when the mission system is on and the sub-mission is defined in the resources, its real state is used.

The sub-mission data of `8048208` / `8048209` is not in the current resources (`ReportTruncatedMissionGraphs` names them at startup), so with the mission system on the guide mission cannot progress normally.

## Limitations

- The entrance part was read from client Lua bytecode and C# disassembly. The server regression only checks the data sent (map group list, scene mission info); the button and the entrance NPC need confirmation on a real client.
- Inferred rather than confirmed by packet capture: fixed candidates (a node's Fixed* lists) being the reward after that node, elite nodes giving an extra curio, story levels keeping 0 stars, challenge stars computed from the damage share, `NBNMHLIFCMF` handled as give up, the purpose of `MDOHKDNNDPI`.
- The two values added to `MissionFinishTypeEnum` appear on the cached `FinishWay` rows only after `Resource.bin` is regenerated with the current code; in an older cache these missions have FinishType `Unknown` and the event missions do not progress.
- Not verified on a real client.
