# Galactic Gala of Peace: Road to Fame (RealityShow)

English | [简体中文](reality-show.zh-CN.md)

> **Re-enabled on retail 4.7.0.** The field layout comes from the retail client dump and has not been verified on a live client. The command IDs and field tags below describe upstream 4.8; use `KcpSharp/CmdIds.cs` and `Proto/4.7` for retail.

This document describes the server side of the RealityShow challenge event added in 4.8: its configuration, its unnamed protocol, the battle statistics it settles from, and its event missions. Read it before changing the event or re-exporting its challenge table.

## Overview

A challenge event added in 4.8 (client OSBETAWin4.7.51). Client module `RPG.Client.ActivityRealityShow`, panel `50143`; each of the five challenges has its own activity module `5014301`–`5014305`; stage type `RealityShowActivity` (`StageType` 40). Each challenge consists of two battles, both run on the client:

- Warm-up stage (StageID1, endless waves `_StageInfiniteGroup`, limited turns): break as many special shields as possible. The number of shields broken is converted into a buff level (0–3) using the `BuffUpNumList` thresholds.
- Final stage (StageID2, a single boss with 9999 HP bars, limited turns): the team carries the first N buff levels earned in the warm-up and is rated C / B / A / S / SS by the number of HP bars broken (`ActivityRealityShowHPPunctured` = 0 / 100 / 300 / 600 / 1200); with equal HP bars, the one that consumed less action value is better.

The server handles: the challenge record (warm-up result, buff level, best final result, last lineup), starting and settling battles, resetting the warm-up, the friend ranking, and event missions.

| Content | Path |
| --- | --- |
| Gameplay logic (player component) | `GameServer/Game/Activity/RealityShow/RealityShowManager.cs` |
| Request handlers | `GameServer/Server/Packet/Recv/RealityShow/HandlerRealityShow.cs` |
| Responses | `GameServer/Server/Packet/Send/RealityShow/PacketRealityShow.cs` |
| Save table `PlayerRealityShow` | `Common/Database/RealityShow/PlayerRealityShowData.cs` |
| Configuration | `Config/Custom/RealityShow.json`, model `Common/Data/Custom/RealityShowConfig.cs` |
| Event missions | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerRealityShow.cs` |
| Battle integration | `BattleInstance.RealityShowContext` (lineup override), `BattleManager.EndBattle` (event lineups do not write back HP and do not teleport) |
| Schedule | `5014301`–`5014305` / panel `50143` in `Config/Custom/ActivityConfig.json` |
| Regression | `scripts/LoginSceneRegression/RealityShowRegression.cs` |

## Configuration

The challenge table `ExcelOutput/ActivityRealityShowConfig` (resource `39e08083a365604a`, row type `ActivityRealityShowConfigRow`) is not in the community name list, so the resource export skips it. `Config/Custom/RealityShow.json` is obtained by decoding it by name with Demiurge:

```sh
demiurge res decode --game <client root> --namemap <file containing BakedConfig/ExcelOutput/ActivityRealityShowConfig.bytes> ExcelOutput/ActivityRealityShowConfig.json
```

| Challenge | Module | Warm-up Stage | Final Stage | Boss | Buffs | Thresholds |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 5014301 | 4320046 | 4320146 | 5034013 | 3250010–3250012 | 5 / 15 / 24 |
| 2 | 5014302 | 4320036 | 4320136 | 4035012 | 3250007–3250009 | 5 / 15 / 24 |
| 3 | 5014303 | 4320026 | 4320126 | 5014016 | 3250004–3250006 | 4 / 12 / 20 |
| 4 | 5014304 | 4320016 | 4320116 | 3024034 | 3250001–3250003 | 5 / 15 / 24 |
| 5 | 5014305 | 4320056 | 4320156 | 5014025 | 3250013–3250015 | 4 / 12 / 20 |

In `StageConfig`, 43200x0–43200x6 and 43201x0–43201x6 are seven variants by world level, but the client's `BattleResultProcess._IsRealityShowFirstStage` and `RealityShowHelper.GetBattleAreaIDByStageID` only recognise the exact StageIDs in the configuration table (the level 95 shown in the UI also comes from it). The server therefore uses the StageIDs from the configuration directly and does not map them by world level. The warm-up shield buffs are bound by the stage's own `_BindingMazeBuff`; the final-stage buffs are put into `SceneBattleInfo.buff_list` by the server according to the buff level.

Another table, `ExcelOutput/RealityShowFriend` (`01de9b0a29982f5b`, NPC friend ranking), is used only by the client.

## Protocol

The command directory 10785–10799 is unnamed as a whole; messages and fields keep their obfuscated names. The meanings come from the client code: requests are sent by the network service `LDFEDNEMOCD`; the data service is `LBBBMBMCADE` (interface `MINAKLCBJMO`); the auto-properties of the challenge data model `MJCMGOEMKCC` keep their real names (`<ShieldBreakCount>k__BackingField` and others), and matching property declaration order with getter order and storage offsets gives the field meanings.

| cmdId | Message | Direction | Fields |
| --- | --- | --- | --- |
| 10786 | `CKMHMAJPJEP` | Request: get data (sent at login) | none |
| 10799 | `PAMDEKPLLEL` | Response | `OGDEMOAKJOK` challenge list (`LFIKNHJBDBJ`), retcode |
| 10798 | `OHFEIJJDJIE` | Request: start challenge | `challenge_id`, `LAJAGHBKIJH` IsFirstStage (warm-up), `avatar_list` lineup (`KPNHGDGLICN {avatar_type, id}`) |
| 10792 | `IFMNCFKBEGL` | Response | `challenge_id`, retcode, `NEMJKLMIJOL` challenge data, `KHBJOAHCCCE` lineup, `battle_info` battle |
| 10794 | `IOOHJLNKKKD` | Request: reset warm-up (`RealityShowChallengeViewModel._OnResetFirstStage`) | `challenge_id` |
| 10795 | `FKHNFMFIKGL` | Response | retcode, `NEMJKLMIJOL` challenge data after the reset |
| 10797 | `NKEMNFGCBLO` | Request: friend ranking (the client does not wait for the reply) | `challenge_id` |
| 10796 | `JBPOKKNFOKM` | Response (the client subscribes to it as a push and matches it by challenge ID) | `MJCCFIINAJC` ranking entries (`CFIIJJEGKHC`), `challenge_id`, retcode |
| 10785 | `NOEGOMIJDAH` | Notification: settlement | `challenge_id`; oneof: `BCLOOPGAIKJ`(9) warm-up result, `OEFFDKDCIMC`(2) final result |

Challenge data `LFIKNHJBDBJ`: `challenge_id`, `KHBJOAHCCCE` last lineup, `LMINNGCDDOK` IsFirstStageFinished, `MNIBAEDPCHA` ShieldBreakCount, `HAGDKKBAAKK` BuffLv, `CNEJKLABHDG` MaxHpBarBreakCount, `LIHEPONEGOB` ConsumedActionValue (double).

Warm-up result `CGCIGGBIGAL`: `HAGDKKBAAKK` BuffLv, `MNIBAEDPCHA` ShieldBreakCount (the client overwrites its values directly and sets IsFirstStageFinished). Final result `HAIEHHIHIGC`: `NKIIHODEFBP` HpBarBreakCount, `FMBFAPOCIDF` ConsumedActionValue (double). The client decides on its own whether this is a new record, in `MJCMGOEMKCC.HOAHFAPHDND`: 0 HP bars does not count; more HP bars is a new record; with equal HP bars, less consumed action value (`UINumberUtils.DelayInt`) is a new record.

Friend ranking entry `CFIIJJEGKHC`: `uid`, `CNEJKLABHDG` best HP bars, `LIHEPONEGOB` consumed action value, `OEOPMBHFBBP` characters fielded.

Lineup: trial characters use `AvatarTrialType` + SpecialAvatar ID (which must be in the challenge's `SpecialAvatarList`); owned characters use `AvatarFormalType` or `AvatarUpgradeAvailableType` (which replaces the trial character with the same name) + character ID. An observed client request: `{challengeId: 2, LAJAGHBKIJH: true, avatarList: [1407, 1413, 1415, 1409] × AvatarUpgradeAvailableType}`.

When the start-challenge response succeeds, the client (`CCFKBJDAMNJ.HCJDPFKJOHE`) first updates the challenge from `NEMJKLMIJOL` and `KHBJOAHCCCE`, then passes `battle_info` to `AdventureModule.CreateBattleServerCheckResult` to enter the battle; with a non-zero retcode it shows the generic enter-battle error.

## Battle statistics

The client sub-mode `KKCDPIJCADB` (`ActivitySubModeType.RealityShowActivity` = 10) writes its counters in `GenStatistics` into `MFOHGCNOMLB` (6, `IHDKFBDLNGL`) of `BattleStatistics.MHPDKEDHELL` (37, `GBJMLKNLEPD`):

| Field | Meaning |
| --- | --- |
| `MNIBAEDPCHA`(1) | Special shields broken (warm-up; triggers `ActivityRealityShowShieldBreakCountUpdate`) |
| `KMBPEBAFDLF`(2) | HP bars broken (final; triggers `ActivityRealityShowHpBarBreakCountUpdate`) |
| `LGIFBIOJKFH`(3) | Remaining action value reported by the boss battle event `BattleEventAbility_RealityShow_Boss_ReportLeftDelay`; not used by the server |

The server settles on a won `PVEBattleResultCsReq`: the warm-up records the shields broken and the buff level; the final takes the HP bars broken, with the consumed action value taken from `Stt.total_delay_cumulate`. The settlement notification is sent before `PVEBattleResultScRsp`, and the client's settlement screen reads it. A loss or quitting does not settle.

## Event missions

The FinishWay types take the values of the client's `MissionFinishType`: `RealityShowBuffNum` = 470011, `RealityShowResult` = 470012, `RealityShowAllResult` = 470013 (no FinishWay uses it in 4.7.51). Missions 6091001–6091028: `ParamInt1` is the challenge ID, `ParamInt2` the level (the buff level for BuffNum; the rating for Result: 1 B / 2 A / 3 S / 4 SS); reaching or exceeding it finishes the mission.

In a `Resource.bin` generated before these three enum members were added, these FinishWay rows have type `Unknown` and the missions do not progress; the resource cache has to be rebuilt with the current code (see [Rebuilding the resource cache from a client export](../operations/resource-cache-from-client-export.md)). The regression checks the FinishWay types loaded from the cache without changing them.

## Not covered

- Not verified on a real client: battle behaviour after starting, the settlement screen, whether the final-stage buffs take effect, badge and time-limited reward claiming.
- `RealityShowAllResult` has no matching FinishWay; no handler is implemented for it.
