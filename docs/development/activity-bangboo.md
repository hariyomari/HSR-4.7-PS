# Bangboo Crossover Event "Planarcadia Reverie" (ActivityBangboo)

English | [简体中文](activity-bangboo.zh-CN.md)

> **Not built on this branch.** ActivityBangboo is excluded from the build (`<Compile Remove>` in [`GameServer/GameServer.csproj`](../../GameServer/GameServer.csproj)) because its commands and messages are missing from, or differ in, the active 4.7 protocol. Its `Config/Custom/ActivityBangboo.json` is still shipped and loaded.

This document describes the server side of the Bangboo crossover event added in 4.8: its unnamed protocol, where its configuration comes from, the rules the server enforces, its event missions and the evidence behind each mapping. Read it before changing the event or re-exporting its configuration for a new client.

## Overview

The event was added in 4.8 (client OSBETAWin4.7.51). Client module `RPG.Client.ActivityBangboo`, event panel `50147`, activity modules `5014701` (event) and `5014702` (permanent). Stage battles are the client's Bangboo battle mode (`BangbooVsChenling` in GameCore) and a whole run is played on the client. The server only stores progress: unlocked and cleared stages; for the Infinity holes, whether they are open, the best wave of each hole and a run that can be continued (the next wave and the board archive saved by the client); posts read and liked; and the performance groups watched.

| Content | Path |
| --- | --- |
| Gameplay logic (player component) | `GameServer/Game/Activity/ActivityBangboo/ActivityBangbooManager.cs` |
| Mission events | `GameServer/Game/Activity/ActivityBangboo/ActivityBangbooEvents.cs` |
| Request handlers | `GameServer/Server/Packet/Recv/ActivityBangboo/HandlerActivityBangboo.cs` |
| Responses and notifications | `GameServer/Server/Packet/Send/ActivityBangboo/PacketActivityBangboo.cs` |
| Save table `PlayerActivityBangboo` | `Common/Database/ActivityBangboo/PlayerActivityBangbooData.cs` |
| Configuration | `Config/Custom/ActivityBangboo.json`, model `Common/Data/Custom/ActivityBangbooConfig.cs`, read at startup by `Common/Data/CustomConfigFiles.cs` |
| Event missions | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerActivityBangboo.cs` |
| Schedule | `5014701` / `5014702` in `Config/Custom/ActivityConfig.json`, panel `50147` |
| Regression | `scripts/LoginSceneRegression/ActivityBangbooRegression.cs` |

## Protocol

In the 4.8 dump the whole command directory `MFNJLCNMBON` (10602–10640, 15 commands) is unnamed. Messages and fields keep their obfuscated names and were not written into the 4.8 master. Direction, pairing and field meanings all come from the client code; the evidence is at the end of this section. In every response `JKCOAIGJMMM` is the retcode.

| cmdId | Message | Direction | Client sender / reader | Fields |
| --- | --- | --- | --- | --- |
| 10604 | `FDFNDGOOCGJ` | Request: get data | `CBAPNPOMOII.RefreshAsync` | none |
| 10627 | `BGCOEFMFEHA` | Response | `CBAPNPOMOII.CHNCBFKGKGM` and others | `FJCAKOIDONG` data (`ABCMHMIHJBO`) |
| 10634 | `MGJEKPCMNDD` | Request: read post | `ReadAsync` | `CBGODLHCLPK` post ID |
| 10632 | `MHJNBHCDPHK` | Response | `CBAPNPOMOII.CMEEJACADNG` | `AMFEFIDDMDA` post (`IHNJGNPKKOA`); the post ID must match the request |
| 10616 | `HBHPDMLDLBL` | Request: like / unlike | `SetLikeAsync` | `CBGODLHCLPK` post ID, `IAJAOMAKAHA` liked |
| 10637 | `NIIFNPFEKIG` | Response | `CBAPNPOMOII.NGOIFHEHHAJ` | `AMFEFIDDMDA` post |
| 10606 | `HKIFIOMLLCE` | Request: start stage | `MAOBJJGOCJB._StartPreparedGameAsync` | `IJAPHCOHMIN` stage, `OEDLOIPDGKG` selected Bangboo |
| 10640 | `BOBEICIPCNK` | Response | as above, `DMHBBCKDLKA.KIGKKCNBNAN` | `IJAPHCOHMIN` stage (must be echoed and non-zero), `OJHNAJCAKNB` GameTransNo (must not be blank), `IFDOJGKJEBN` Infinity run (`JDKBCJPLNHF`, Infinity stages only) |
| 10638 | `BNMEGDLCIKO` | Request: performance finished | `OOOOHFOLEIA._FinishPerformanceAsync` | `CIGGAEJNEFM` performance group ID |
| 10602 | `PPHGPJDEHDA` | Response | `CBAPNPOMOII.PPHLLOLDENN` | `CIGGAEJNEFM` echoed performance group |
| 10620 | `MBCCKEIJAGF` | Request: settle | `NFFAPNKNMBF.LPCDOLBHIKN` (`_SettleAsync`, `SaveAsync`, `_ExitAsync`, `AbandonInfinityRunAsync`) | `IJAPHCOHMIN` stage, `OJHNAJCAKNB` GameTransNo, `PKKMAAIJEOD` reason (`FDHNDGJCOAC`), `FFCBOJAACMD` statistics (`MPAJNPPLKMP`), `BBEMIGELGJC` board archive (`JDCCDGMJMII`) |
| 10633 | `EBHKOMEILOJ` | Response | `DMHBBCKDLKA._SettleAsync`, `NNIFKNCJELA.JKGONFAKGLB` | `IJAPHCOHMIN` echoed stage (on a mismatch the client skips the settlement screen), `ENGHMIAMPLG` rewards (`ItemList`), `HPKEGPGPPOK` the hole's all-time best wave |
| 10635 | `FFPHBNAGJNP` | Request: end Infinity run | `_SettlePreparationAsync`, `_EnsureEndlessRunClearedAsync` | `IDCGICECPHO` final statistics (`IHFFJHNEFMF`) |
| 10611 | `EEJOPBDAHNJ` | Response | as above | retcode only |
| 10622 | `LIEHHGLAEHG` | Notification | `CBAPNPOMOII.BOEOIIPLFDA` | `DGNGMJMPFGK` change list (`CAGDONKMOGB`) |

### Data `ABCMHMIHJBO`

| Field | Meaning | Where the client uses it |
| --- | --- | --- |
| `EEJKJMCMJBA`(2) | Unlocked stages | `OEOKJAHHFOJ.OIGDABFJCHJ(unlocked, finished)`: `IsUnlocked = in either list` |
| `ICHOFAHCIIF`(6) | Cleared stages | Same call, `IsFinished = in this list`; unlocked / finished for story holes is derived from their stages |
| `IEKHMACAFMK`(3) | Performance groups watched | `BDPNKGJPIPK.ADFFOCBJIII`: `IsRead` of each group |
| `GILOPNKGNAA`(8) | Infinity holes open | `CPMDJHNLEMG.LLEPNCCEILB`: `IsUnlocked` of every hole with `HoleType=Infinity` |
| `LOBHIIILPCH`(11) | Infinity best `MJMPBCBGJHH {IKJPIFBFEAG hole, EMMGMLIBHGP MaxWave}` | Same call, the hole's `MaxWave` |
| `IFDOJGKJEBN`(4) | Infinity run that can be continued `JDKBCJPLNHF {IKJPIFBFEAG hole, OHIDKMKBIGJ wave, BBEMIGELGJC archive}` | Same call, the hole's `CurrentWave`; the archive is converted by `JCLOJAFLJPN` into `AHKCPHPIMHB` (Board, Statistics, SelectedBangbooIDs, HoleID, CurrentLevelID, CurrentEnergy). When empty, the client treats it as 0 |
| `AAHPCIOOLHB`(15) | Posts `IHNJGNPKKOA {CBGODLHCLPK post, BNBCGFOBIPH read, IAJAOMAKAHA liked}` | Post store |

### Change notification `CAGDONKMOGB`

A oneof: `CEMIOADEMHA`(9) newly unlocked stage (`NPFMFIKIDAC.LNMPJKPEHAE`), `FADGOOJBAKE`(6) newly unlocked Bangboo (`FKDJGPFCDJK.LNMPJKPEHAE`), `GILOPNKGNAA`(10) Infinity holes opened, `BPJABGNNHDO`(14) post. The client marks each item as "new" (`GAKPNLDHION`, `ADOLOAABELD`, `EGKDNJKPDFL`, `GBNMBAMHPJF`) and then unconditionally calls `RefreshAsync` to fetch the data again.

### Settlement reason `FDHNDGJCOAC`

`SaveAsync` always sends 1, `_ExitAsync` and `AbandonInfinityRunAsync` always send 2, and `_SettleAsync` passes the game-over reason through. In `NNIFKNCJELA.JKGONFAKGLB`, a non-Infinity stage gets `Kind = reason == 1 ? Victory : Failure`; an Infinity stage always gets `EndlessFinished`, with `HPKEGPGPPOK` used as `EndlessScore.HistoricalMaxWave`.

### Statistics `MPAJNPPLKMP`

Filled one by one by `NNIFKNCJELA.ENGIEAAAFDO` from the real-named properties of `DEAHBFLFLED` / `HKICNHMIMAG`: `CPFCKNMOKDL`(1) CartTriggerCount, `HPEODGIDKAN`(2) EnergyFromBangboo, `BJPIBINDNLF`(5) BangbooTotalValue, `ACGMHLIGFBA`(7) EnergyAtEnd, `LALKFADHJJL`(8) CostTime, `CBBALOIKCPC`(10) CurrentWave, `CBIBKFKHLMI`(11) TacticsBangbooKillCount, `IBJNHACMKLE`(12) EnergyFromLevel, `FPOCOJHBLPI`(14) MaxSimultaneousBangbooIdCount, `BEKNMJFKHHN`(15) / `PIIKPCFHGFI`(1219) CostPower, `EJCLFCEKFDB`(778) EvolveBangbooCount, `JMHAFKMFLII`(900) CombineBangbooCount, `LGJGNFBJKIH`(2036) PlaceBangbooCount, `CLICEAKBDJK`(173) BangbooPlaceCountList, `AECMCJCCMFO`(251) BangbooDeathCountList, `FDNLKGOOHHF`(1078) BangbooCountAtEndList, `FLLNGJGFFAF`(1596) BangbooCombineCountList, `IBAGICECKBG`(178) StartBangbooIdList. List entries `MMPEJCJEEHO`: `HCDEOKNIGKG` Count, `BDCOFIOCJFO` Star, `FFPMBJPCAGG` presumed to be the Bangboo ID.

### Unrelated message

`KKNAAFIBLLH` (10234) has nothing to do with this event: it belongs to command directory `MCDALMNBOPN` together with `DrawScRsp` and `EIEOFMBKAHM`, and is used on the `DepletablePoolModule.OnLoginFinish` side.

### Evidence and method

- Demiurge `dump` produces dump.cs; a small disassembly tool that references Demiurge (PeImage + Metadata.Usages + Iced) then annotates it with calls and type and method references.
- Request direction: the seven requests are constructed only in the network service class `NFFAPNKNMBF`; the seven responses are registered in the packet registry `<>c::<.cctor>b__2_137..143`; `LIEHHGLAEHG` is dispatched by type in `CBAPNPOMOII.BOEOIIPLFDA`, so it is a notification. Pairing comes from the signatures of the `NFFAPNKNMBF` methods (for example `UniTask<BOBEICIPCNK> OFHMHPKEECI(uint, IReadOnlyList<uint>)`).
- Real method names come from the callers' async state machines and lambdas (`<RefreshAsync>d__16`, `<ReadAsync>d__7`, `<SetLikeAsync>d__8`, `<_StartPreparedGameAsync>d__83`, `<_FinishPerformanceAsync>d__12`, `<_SettleAsync>d__76`, `<SaveAsync>d__3`, `<_ExitAsync>d__74`, `<AbandonInfinityRunAsync>b__15_0`, `<_EnsureEndlessRunClearedAsync>d__88`, `<_SettlePreparationAsync>d__75`); the state machine fields keep the real parameter names (`levelID`, `gameTransNo`, `reason`, `completedLevelID`, `groupID` and others).
- Field names: the obfuscator produces the same obfuscated name for the same original identifier. For every class in the dump that has `<X>k__BackingField`, reading the storage offset of each property getter gives an "obfuscated property name → real name" table (about 5000 entries), for example `MNBBCAMDGDI` = IsUnlocked, `HCOFMFKFMDK` = IsFinished, `EMMGMLIBHGP` = MaxWave, `NBMAGDOJDFD` = CurrentWave, `BNBCGFOBIPH` = IsRead, `KGEJAOIMOBK` = IsLiked, `OJHNAJCAKNB` = GameTransNo, `FFCBOJAACMD` = Stt, `BBEMIGELGJC` = Archive. Fields whose names do not match (`EEJKJMCMJBA` and others) were determined by disassembling the data-dispatch code and comparing the storage offsets of the proto properties.
- All validation conditions come from the disassembly: the start response checks the retcode, that `IJAPHCOHMIN` matches the request and that `OJHNAJCAKNB` is not blank; for Infinity stages (`BangbooPreparationKind.Infinity`) `ODAGFOCMOBJ.KJPFECFJECA` also checks that the run's hole matches the stage's hole, that the wave is non-zero and equals the stage's index in the hole + 1, and that the archive's `CurrentLevelID` (when the archive is not empty) equals this stage; post and performance responses check the echoed ID.

## Configuration source

The stage, hole, Bangboo and performance tables are in the client's ExcelOutput, but their paths are not in the community name list, so the resource export does not produce them, and their row types are obfuscated. `Config/Custom/ActivityBangboo.json` was produced by decoding these resources with Demiurge by row type (array, start offset 1) and cross-checking the IDs; `_source` in the file records the asset hashes:

| Table | Asset hash | Row type | Columns the server uses |
| --- | --- | --- | --- |
| `BangbooLevel` | `d01b5c63dfe31e77` | `NJPNGGOMFIG` | ID; `ENGHMIAMPLG` reward (loaded by the client as `RewardID`); `BDGPDCKLFNF` unlock sub-mission |
| `BangbooHole` | `8a1a75b7512b217b` | `PLCFNJFELLE` | ID; `CFPOHAONOLM` HoleType; `ENIDMPHBPKF` LevelIDs |
| `BangbooConfig` | `71578b04fa76bb45` | `OHIEDOGFAEM` | ID; `GMPGDEINODK` Type; `AAMKNOOMEOO` UnlockLevel |
| `ActivityBangbooPerformance` | `9046f1b03d347d04` | `JPGKFBDAPLM` | `LLDCHLHNADA` GroupID |

Cross-checks: a stage's initial Bangboo column (`ANKIMCODPOE`) only contains Bangboo unlocked by earlier stages (for example Bangboo 4 has UnlockLevel 101 and first appears in 102). `BDGPDCKLFNF` is loaded by the client as `UnlockSubMissionID` in post rows and performance group rows (row offset 0x34 → model 0x80, 0x1C → 0x24); the stage model does not read this column, so it is used only by the server. `BangbooPost` / `BangbooPostComment` (`5b5accf53acf3ba2` / `e01022c2094c0e57`) are empty in this version; post IDs are taken from the ParamIntList of the `BangbooReadPost` missions.

The file is not part of `Resource.bin`; it is read from `Config/Custom` on every start. After a client update it has to be exported again the same way.

## Rules

- Stage unlocking: the stages of the story holes (1, 2, 3) form one chain ordered by hole ID and position in the hole (101 → … → 304); each stage needs the previous one cleared. Stages that are in no hole (100 tutorial, 305/306 boss phases) are always open. Stages of the Infinity holes (4, 5, 6) open with Infinity. When `requireUnlockSubMission` is true, the stage's `unlockSubMissionId` must also be finished; this is off by default because the event mission line (main mission 8048117) is incomplete on this server, and turning it on would leave stages impossible to unlock.
- Infinity opening: the first clear of `infinityUnlockLevelId` (default 304) opens all Infinity holes (the client has only one switch).
- Starting a stage: stages that are not unlocked and Bangboo that are not unlocked or unknown are rejected; a GameTransNo is generated and the current game is recorded. Wave 1 of an Infinity hole starts a new run (the old run is discarded and counted towards the best); later waves must continue the saved run, and the response carries the run and its archive.
- Settlement: it must match the current game (stage and GameTransNo), otherwise it fails; the same game cannot be settled twice. Reason 1 counts as a clear: the first clear grants the stage reward (RewardData) and works out newly unlocked stages, Bangboo whose UnlockLevel is this stage, and the Infinity opening; the change notification is sent after the response.
- Infinity run: reason 1 records the waves passed as the best, saves the archive sent by the client and moves to the next wave (the run ends after wave 12); reason 2 (leave) keeps the progress saved last time; any other reason ends the run. `FFPHBNAGJNP` ends the current run (it also succeeds when there is no run).
- Posts: the ID must be in `postIds`; reading marks the post as read, and like / unlike are recorded separately. Performance groups: the ID must be in `performanceGroupIds`; repeated reports are recorded once.

## Event missions

The FinishType values are taken from the client's `MissionFinishType` enum and added to `Common/Enums/Mission/MissionFinishTypeEnum.cs`:

| Value | FinishType | Triggered by the server when |
| --- | --- | --- |
| 480500 | `BangbooLevelFinish` | A stage is cleared for the first time; ParamIntList holds stages (the n-th stage in an Infinity hole is the n-th wave) |
| 480501 | `BangbooReadPost` | A post is read for the first time |
| 480502 | `BangbooDeployCount` | Accumulated on every settlement: placements in BangbooPlaceCountList whose type is ParamInt1 (1 combat, 2 tactics) and whose star is in ParamIntList (empty means any) |
| 480503 | `BangbooMaxDeployCountInLevel` | The same count for a single game, maximum taken |
| 480504 | `BangbooFinishNpcGroupPerformance` | A performance group is watched for the first time |
| 480505 | `BangbooTacticsKillCount` | TacticsBangbooKillCount accumulated on every settlement |
| 480506 | `BangbooMaxEnergyAtWin` | EnergyAtEnd on a clear, maximum taken |
| 480507 | `BangbooMaxSimultaneousIdCount` | MaxSimultaneousBangbooIdCount of the stages in ParamIntList, maximum taken |

These types are used by event missions 6094001–6094105, achievements 4055801–4055805 and missions 2000498 / 2000499. Mission texts (such as "Upgrade Combat Bangboo #4 time(s) in total", "Summon 4-star Combat Bangboo", "Defeat the 3rd wave of enemies in any area in Infinite Mode") were used to confirm what the parameters mean. The new enum values appear on the cached `FinishWay` rows only after `Resource.bin` is regenerated with the current code; in an older cache these rows have FinishType `Unknown` and the missions do not progress.

## Regression

The `ActivityBangbooRegression` section of `scripts/LoginSceneRegression` goes through the real handlers and covers: configuration and schedule, handler and FinishType registration; data of a new account; rejection of locked stages, unknown stages and Bangboo that are not unlocked; rejection of a GameTransNo that does not match the stage and of a repeated settlement; leaving without a clear; first-clear rewards and the change notification (new stage, new Bangboo), and no reward on a replay; story-chain unlocking up to 304, then opening Infinity with a notification; Infinity wave 1 archive, starting wave 2 with the archive, leaving keeps progress, failure ends the run, clearing three waves updates the best, `FFPHBNAGJNP` ends a run; reading / liking posts and unknown posts; performance groups; mission progress driven by statistics; the save table written to SQLite and read back. The `Resource.bin` in the repository predates the new enum values, so the regression sets the relevant FinishWay rows to the matching types in memory before checking, and restores them at the end.

## Limitations

- Inferred rather than confirmed by packet capture: the meaning of `FDHNDGJCOAC` 3 and 4 (handled as "other reason", which ends an Infinity run); reason 2 keeping progress in Infinity (`_ExitAsync` and `AbandonInfinityRunAsync` both send 2 and cannot be told apart; abandoning a run relies on the client's following `FFPHBNAGJNP`); the best wave counted as the number of waves passed; the story-chain unlock order and the stage that opens Infinity (304); statistics from leave / failure settlements also counting towards missions; `BangbooDeployCount` counting the placement list rather than the combine list; `MMPEJCJEEHO.FFPMBJPCAGG` being the Bangboo ID.
- Not implemented: the post branch of the change notification (the server has no posts that change on their own); turning the mode off according to the event schedule (as with DiceyDungeon, whether the event is open is not checked).
- The post tables are empty in this version, so the client may not show any posts.
- Not verified on a real client.
