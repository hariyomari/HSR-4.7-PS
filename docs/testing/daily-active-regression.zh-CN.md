# 每日实训回归

[English](daily-active-regression.md) | 简体中文

`DailyActiveRegression` 检查每日实训、活跃度档位、每日委托，以及每日实训任务的事件型完成条件。修改这些功能或每日刷新逻辑后运行。

项目：[`scripts/DailyActiveRegression/`](../../scripts/DailyActiveRegression/)

## 运行方式

从仓库根目录执行：

```powershell
dotnet run --project scripts/DailyActiveRegression/DailyActiveRegression.csproj -c Release
```

选项：

- `--cache <路径>`：读取其他资源缓存，而不是 `Config/Resource.bin`。
- `--stale-cache <旧缓存> --resources <原始资源目录>`：两者同时给出时，额外验证在原始 `DailyQuest.json` 可用时，缺少每日表的旧缓存会被拒绝。

## 前提条件

- 默认读取当前 `Config/Resource.bin` 的副本，从不修改原文件。
- 缓存必须包含 `DailyQuest`、`DailyMissionData`、`DailyMissionCount` 三张表以及 `CocoonConfig` 的 `CocoonType`/`FarmType` 字段，否则第一项检查失败并退出。
- 使用临时 SQLite 数据库和隔离配置，不启动网络服务，不读取运营配置或任何玩家数据库。

## 覆盖范围

### 日期边界

- 服务器时区 03:59 仍属于前一天，04:00 开始新的一天。
- 刷新时刻为 0 的情况与跨月边界。
- `ServerOption.DailyResetHour` 越界时钳制到 0–23。

### 任务抽取

- 使用 FNV-1a-64 + SplitMix64 + Fisher–Yates，结果与运行时无关。
- 同一 (uid, 日期, nonce) 抽到同一组 8 个任务；池的顺序不影响结果；nonce 递增后抽到不同组合。
- 已删除的 `DailyQuest` 行不会入池。QuestType 5 不再被条件自动接取。

### 活跃度与档位

- 领取实训任务奖励会发放物品 23 并计入当日活跃度；`GetItem(23)` 返回当前值。
- 未知档位返回 4101，重复领取返回 4102，活跃度不足返回 4103。
- 一键领取按档位升序处理并合并奖励；重复一键领取不发放任何奖励。
- `TakeQuestOptionalReward` 收到非 0 的可选奖励 ID（无关的奖励 ID 或直接的物品 ID）时返回 `RetReqParaInvalid`，不发放任何奖励，任务保持已完成；ID 为 0 时只发放该任务的 `RewardID`，且只发放一次。

### 跨日

- 新周期清空活跃度和已领档位。
- 前一天未入选的任务关闭，未领取的奖励作废；当天入选的任务（包括之前已领取的）重置为进行中、进度 0。
- 与 `/daily reset` 等价的 nonce 重抽。
- `FinishQuest` 型实训（2100156/2100159）入选时重新激活其子任务，不会被昨日的完成状态直接满足。

### 事件型完成条件

- 心跳（空参数）和无关参数不推进任何事件型任务。
- `AnyRelicLevelUp`、`AnyEquipmentLevelUp`、`AnyAvatarLevelUp`、`RogueFinishCnt`、`ResolveRelic`、`AnyItemComposeCount`、`ExpeditionSetOrTakeReward` 只由各自的事件推进。
- `ChallengeFinishCnt` 按挑战类型过滤。

### 花萼与其他副本

- 战斗失败和凝滞虚影战斗（只有 `CocoonWave`、没有 `CocoonId`）不计数。
- 金色花萼完成 `CocoonFinish` 与 `FarmTypeCocoonFinish [6,7,8]`；赤色花萼完成 `[5]`；侵蚀隧洞完成 `AnyCocoonFinish [3]`。

### 每日委托

- 完成每日委托后达成 `FinishTodayDailyMissionCnt`；同一委托每个周期只计一次。跨日时当日计数清零，累计计数保留。
- 对开启任务系统的玩家：未满足解锁任务时不发放委托；强制发放会补足到 `DailyMissionCount.DailyCount`；同一天重复发放不追加。完成的委托计入计数；跨日后已完成的委托恢复为可接取并重新发放。

### 存储

- 每日实训数据写入独立的 `DailyActive` 表。

## 预期结果

运行结束时输出 `Daily active regression checks: <n>, failures: 0`，退出码为 0。失败的检查会列在这一行下面。

## 已知缺口

- 这不是客户端测试。面板显示、领取动画、任务追踪，以及客户端从 `DailyActiveInfoNotify` 和 `GetDailyActiveInfoScRsp` 读取的值都需要实机验证。
- 已接入计数事件的条件：`UseMazeSkill`（施放秘技）、`PropDestruct`（可破坏物）、`WinBattleWithAssistAvatarCnt`（携带支援角色获胜）、`RogueExploreRoomFinishCnt`（模拟宇宙房间）。`AnyFarmElementFinish` 只计凝滞虚影胜利（排除花萼）。`UseItem` 按实际消耗数量计数（消耗开拓力计为物品 11）。
- 尚未实现、入选后无法完成的条件：`ExcellentEnterBattleAndWin`、`ItemCompose`、`TakePictureCnt`、`UseItemWithType` 等。
- 2100156 的货币战争子任务（`GridFightFinishGamePlay`）同样未实现，2100156 只能通过差分宇宙子任务完成。
