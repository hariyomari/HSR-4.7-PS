# 任务与战斗额外奖励

[English](progression-rewards.md) | 简体中文

玩家完成主线任务或子任务、或赢得战斗时，服务器可以额外发放物品。额外奖励叠加在官方奖励之上，官方奖励本身不变。设置位于 `Config.json` 的 `ServerOption.ProgressionRewards`，可在后台（设置页的 `rewards` 分组）修改，无需重启。默认关闭。

## 配置（`ServerOption.ProgressionRewards`）

| 键 | 默认值 | 含义 |
| --- | --- | --- |
| `Enabled` | `false` | 总开关。关闭时不发放任何奖励。 |
| `MainMission` | 180 星琼、10000 信用点、1 张星轨通票 | 每完成一个主线任务发放。 |
| `SubMission` | 60 星琼、2000 信用点 | 每完成一个子任务发放。 |
| `BattleWin` | 20 星琼、500 信用点 | 每赢得一场已启用类型的战斗发放。 |
| `IncludeDailyMissions` | `false` | 每日主线任务及其子任务每天重复，只有打开此项才发放。 |
| `IncludeCommandFinishes` | `false` | 在命令中完成的任务（`/mission pass`、`finish`、`finishmain`、`next`、`/unlockall rogue`、MUIP 命令）只有打开此项才发放。 |
| `BattleKinds` | 只开启 `Story` | 哪些战斗类型发放 `BattleWin`，见下文。 |

奖励列表由 `{ "ItemId": n, "Count": n }` 条目组成。后台中按每行一个 `itemId:count` 编辑，未知物品 ID 会被拒绝。4.7 物品表中常用的 ID：`1` 星琼、`2` 信用点、`101` 星轨通票（常驻卡池）、`102` 星轨专票（限定卡池）。

## 任务何时发放

任务从已接取变为已完成时发放，与其官方奖励在同一步骤中。额外奖励并入官方奖励通知，客户端会一起显示。对已完成的任务再次调用完成不会发放。正在重玩的模拟宇宙教程任务不发放，与其官方奖励一致。没有子任务的主线任务不发放主线奖励：它没有可玩的内容，服务器会自行关闭这类条目（仅用于显示的 Gap 条目、空的 Branch 条目）。

被移除后重新接取的任务（例如通过 `/mission rollback` 或 `/mission reaccept`）再次完成时会再次发放。这些命令需要 GM 权限。

## 战斗类型

获胜的战斗按第一条匹配的规则分类：

| 类型 | 规则 | 默认 |
| --- | --- | --- |
| `GmSpawn` | 战斗中的所有怪物都来自 `/spawn` | 关 |
| `CurrencyWars` | 货币战争（GridFight）战斗 | 关 |
| `Finality` | Finality 战斗 | 关 |
| `Trial` | 试用活动关卡、Battle College 战斗或试用活动场景 | 关 |
| `Rogue` | 差分宇宙、rogue magic、正在进行的模拟宇宙或类 rogue 场景 | 关 |
| `Challenge` | 正在进行的忘却之庭 / 虚构叙事 / 末日幻影 / tierce 挑战，或挑战场景 | 关 |
| `Domain` | 拟造花萼 / 侵蚀隧洞 / 历战余响（cocoon）、凝滞虚影（farm element）、任何消耗开拓力的战斗，或遗器刷取场景 | 关 |
| `Raid` | 正在进行 raid 或场景为 raid 平面；包括以 raid 形式运行的剧情副本 | 关 |
| `Story` | 城镇、迷宫、列车或城镇房间场景 | 开 |
| `Other` | 其他（活动与小游戏场景） | 关 |

额外奖励并入战斗掉落列表，因此结算界面会显示。失败或放弃的战斗不发放。

大世界敌人会重生，因此开启 `Story` 时玩家可以反复挑战它们刷取 `BattleWin`。
