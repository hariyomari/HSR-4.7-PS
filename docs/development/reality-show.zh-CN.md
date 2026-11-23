# 星际和平盛典：成名之路（RealityShow）

[English](reality-show.md) | 简体中文

> **已在正式服 4.7.0 协议上重新启用。** 字段布局来自正式客户端的协议转储，尚未经过客户端实机验证。下文的命令 ID 和字段编号记录的是上游 4.8 协议；正式服 4.7.0 的定义以 `KcpSharp/CmdIds.cs` 和 `Proto/4.7` 为准。

本文说明 4.8 新增的 RealityShow 挑战活动在服务端的实现：配置、未命名的协议、结算所依据的战斗统计，以及活动任务。修改该活动或重新导出其挑战表之前请先阅读。

## 概览

4.8（OSBETAWin4.7.51 客户端）新增的挑战活动。客户端模块 `RPG.Client.ActivityRealityShow`，面板 `50143`，五个挑战各占一个活动模块 `5014301`–`5014305`，关卡类型 `RealityShowActivity`（`StageType` 40）。每个挑战分两场战斗，均在客户端运行：

- 预热阶段（StageID1，无限波次 `_StageInfiniteGroup`，限定回合）：尽量多地破除特殊护盾。破盾数按 `BuffUpNumList` 门槛换算成增益档位（0–3）。
- 决赛阶段（StageID2，单个 BOSS，9999 条生命条，限定回合）：携带预热获得的前 N 档增益，按击穿的生命条数评级 C / B / A / S / SS（`ActivityRealityShowHPPunctured` = 0 / 100 / 300 / 600 / 1200），生命条相同时消耗行动值少者更优。

服务端负责：挑战记录（预热结果、增益档位、决赛最佳成绩、上次阵容）、开战与结算、重置预热、好友排行和活动任务。

| 内容 | 路径 |
| --- | --- |
| 玩法逻辑（玩家组件） | `GameServer/Game/Activity/RealityShow/RealityShowManager.cs` |
| 请求处理器 | `GameServer/Server/Packet/Recv/RealityShow/HandlerRealityShow.cs` |
| 响应包 | `GameServer/Server/Packet/Send/RealityShow/PacketRealityShow.cs` |
| 存档表 `PlayerRealityShow` | `Common/Database/RealityShow/PlayerRealityShowData.cs` |
| 配置 | `Config/Custom/RealityShow.json`，模型 `Common/Data/Custom/RealityShowConfig.cs` |
| 活动任务 | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerRealityShow.cs` |
| 战斗接入 | `BattleInstance.RealityShowContext`（阵容覆盖），`BattleManager.EndBattle`（活动阵容不回写血量、不传送） |
| 排期 | `Config/Custom/ActivityConfig.json` 中 `5014301`–`5014305` / 面板 `50143` |
| 回归 | `scripts/LoginSceneRegression/RealityShowRegression.cs` |

## 配置

挑战表 `ExcelOutput/ActivityRealityShowConfig`（资源 `39e08083a365604a`，行类型 `ActivityRealityShowConfigRow`）不在社区名单里，资源导出会跳过它。`Config/Custom/RealityShow.json` 用 Demiurge 按名称解码得到：

```sh
demiurge res decode --game <客户端根目录> --namemap <含 BakedConfig/ExcelOutput/ActivityRealityShowConfig.bytes 的文件> ExcelOutput/ActivityRealityShowConfig.json
```

| 挑战 | 模块 | 预热 Stage | 决赛 Stage | BOSS | 增益 | 门槛 |
| --- | --- | --- | --- | --- | --- | --- |
| 1 | 5014301 | 4320046 | 4320146 | 5034013 | 3250010–3250012 | 5 / 15 / 24 |
| 2 | 5014302 | 4320036 | 4320136 | 4035012 | 3250007–3250009 | 5 / 15 / 24 |
| 3 | 5014303 | 4320026 | 4320126 | 5014016 | 3250004–3250006 | 4 / 12 / 20 |
| 4 | 5014304 | 4320016 | 4320116 | 3024034 | 3250001–3250003 | 5 / 15 / 24 |
| 5 | 5014305 | 4320056 | 4320156 | 5014025 | 3250013–3250015 | 4 / 12 / 20 |

`StageConfig` 中 43200x0–43200x6、43201x0–43201x6 是按世界等级的七个变体，但客户端 `BattleResultProcess._IsRealityShowFirstStage` 和 `RealityShowHelper.GetBattleAreaIDByStageID` 只认配置表里的确切 StageID（界面显示的等级 95 也来自它），因此服务端直接使用配置里的 StageID，不按世界等级换算。预热阶段的护盾 BUFF 由关卡自身的 `_BindingMazeBuff` 绑定；决赛增益由服务端按档位放进 `SceneBattleInfo.buff_list`。

另一张 `ExcelOutput/RealityShowFriend`（`01de9b0a29982f5b`，NPC 好友排行）只由客户端使用。

## 协议

命令目录 10785–10799 整组未命名，消息与字段保持混淆名。含义来自客户端代码：发请求的是网络服务 `LDFEDNEMOCD`，数据服务是 `LBBBMBMCADE`（接口 `MINAKLCBJMO`），挑战数据模型 `MJCMGOEMKCC` 的自动属性保留了真实名（`<ShieldBreakCount>k__BackingField` 等），按属性声明顺序与 getter 顺序、存储偏移对应得到字段含义。

| cmdId | 消息 | 方向 | 字段 |
| --- | --- | --- | --- |
| 10786 | `CKMHMAJPJEP` | 请求：获取数据（登录时发送） | 无 |
| 10799 | `PAMDEKPLLEL` | 响应 | `OGDEMOAKJOK` 挑战列表（`LFIKNHJBDBJ`），retcode |
| 10798 | `OHFEIJJDJIE` | 请求：开始挑战 | `challenge_id`，`LAJAGHBKIJH` IsFirstStage（预热），`avatar_list` 阵容（`KPNHGDGLICN {avatar_type, id}`） |
| 10792 | `IFMNCFKBEGL` | 响应 | `challenge_id`，retcode，`NEMJKLMIJOL` 挑战数据，`KHBJOAHCCCE` 阵容，`battle_info` 战斗 |
| 10794 | `IOOHJLNKKKD` | 请求：重置预热（`RealityShowChallengeViewModel._OnResetFirstStage`） | `challenge_id` |
| 10795 | `FKHNFMFIKGL` | 响应 | retcode，`NEMJKLMIJOL` 重置后的挑战数据 |
| 10797 | `NKEMNFGCBLO` | 请求：好友排行（客户端不等待回复） | `challenge_id` |
| 10796 | `JBPOKKNFOKM` | 响应（客户端按推送订阅，按挑战 ID 匹配） | `MJCCFIINAJC` 排行项（`CFIIJJEGKHC`），`challenge_id`，retcode |
| 10785 | `NOEGOMIJDAH` | 通知：结算 | `challenge_id`；oneof：`BCLOOPGAIKJ`(9) 预热结果，`OEFFDKDCIMC`(2) 决赛结果 |

挑战数据 `LFIKNHJBDBJ`：`challenge_id`，`KHBJOAHCCCE` 上次阵容，`LMINNGCDDOK` IsFirstStageFinished，`MNIBAEDPCHA` ShieldBreakCount，`HAGDKKBAAKK` BuffLv，`CNEJKLABHDG` MaxHpBarBreakCount，`LIHEPONEGOB` ConsumedActionValue（double）。

预热结果 `CGCIGGBIGAL`：`HAGDKKBAAKK` BuffLv，`MNIBAEDPCHA` ShieldBreakCount（客户端直接覆盖，并置 IsFirstStageFinished）。决赛结果 `HAIEHHIHIGC`：`NKIIHODEFBP` HpBarBreakCount，`FMBFAPOCIDF` ConsumedActionValue（double）。客户端用 `MJCMGOEMKCC.HOAHFAPHDND` 自行判断是否为新纪录：生命条为 0 不算；生命条更多为新纪录；相同时消耗行动值（`UINumberUtils.DelayInt`）更少为新纪录。

好友排行项 `CFIIJJEGKHC`：`uid`，`CNEJKLABHDG` 最佳生命条，`LIHEPONEGOB` 消耗行动值，`OEOPMBHFBBP` 出战角色。

阵容：试用角色用 `AvatarTrialType` + SpecialAvatar ID（必须在该挑战的 `SpecialAvatarList` 中），自有角色用 `AvatarFormalType` 或 `AvatarUpgradeAvailableType`（替换同名试用角色）+ 角色 ID。实测的一次客户端请求为 `{challengeId: 2, LAJAGHBKIJH: true, avatarList: [1407, 1413, 1415, 1409] × AvatarUpgradeAvailableType}`。

开始挑战响应成功时，客户端（`CCFKBJDAMNJ.HCJDPFKJOHE`）先用 `NEMJKLMIJOL` 和 `KHBJOAHCCCE` 更新挑战，再把 `battle_info` 交给 `AdventureModule.CreateBattleServerCheckResult` 进入战斗；retcode 非 0 时走通用的进战错误提示。

## 战斗统计

客户端子模式 `KKCDPIJCADB`（`ActivitySubModeType.RealityShowActivity` = 10）在 `GenStatistics` 中把计数写入 `BattleStatistics.MHPDKEDHELL`（37，`GBJMLKNLEPD`）的 `MFOHGCNOMLB`（6，`IHDKFBDLNGL`）：

| 字段 | 含义 |
| --- | --- |
| `MNIBAEDPCHA`(1) | 破除特殊护盾数（预热，触发 `ActivityRealityShowShieldBreakCountUpdate`） |
| `KMBPEBAFDLF`(2) | 击穿生命条数（决赛，触发 `ActivityRealityShowHpBarBreakCountUpdate`） |
| `LGIFBIOJKFH`(3) | BOSS 战事件 `BattleEventAbility_RealityShow_Boss_ReportLeftDelay` 上报的剩余行动值，服务端不使用 |

服务端在 `PVEBattleResultCsReq` 胜利时结算：预热记录破盾数与档位；决赛取击穿生命条数，消耗行动值取 `Stt.total_delay_cumulate`。结算通知在 `PVEBattleResultScRsp` 之前发出，客户端结算界面读取它。失败或退出不结算。

## 活动任务

FinishWay 类型取客户端 `MissionFinishType` 的值：`RealityShowBuffNum` = 470011、`RealityShowResult` = 470012、`RealityShowAllResult` = 470013（4.7.51 没有 FinishWay 使用）。任务 6091001–6091028：`ParamInt1` 为挑战 ID，`ParamInt2` 为档位（BuffNum 为增益档位，Result 为评级 1 B / 2 A / 3 S / 4 SS），达到或超过即完成。

在加入这三个枚举成员之前生成的 `Resource.bin` 里，这些 FinishWay 行的类型是 `Unknown`，任务不会推进，需要用当前代码重建资源缓存（流程见[从客户端导出重建资源缓存](../operations/resource-cache-from-client-export.zh-CN.md)）。回归检查从缓存加载的 FinishWay 类型，不再改写它们。

## 未覆盖

- 未经客户端实机验证：开战后的战斗表现、结算界面、决赛增益生效情况、徽章与限时奖励领取。
- `RealityShowAllResult` 没有对应 FinishWay，未实现处理器。
