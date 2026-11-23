# Daily training regression

English | [简体中文](daily-active-regression.zh-CN.md)

`DailyActiveRegression` checks daily training (每日实训), the activity tiers, daily missions and the event-driven completion conditions of daily training quests. Run it after changing any of these, or the daily reset.

Project: [`scripts/DailyActiveRegression/`](../../scripts/DailyActiveRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/DailyActiveRegression/DailyActiveRegression.csproj -c Release
```

Options:

- `--cache <path>` reads another resource cache instead of `Config/Resource.bin`.
- `--stale-cache <older cache> --resources <raw resource directory>`, given together, also checks that an older cache without the daily tables is rejected when the raw `DailyQuest.json` is available.

## Prerequisites

- The regression reads a copy of the current `Config/Resource.bin` by default and never modifies it.
- The cache must contain the `DailyQuest`, `DailyMissionData` and `DailyMissionCount` tables and the `CocoonType`/`FarmType` fields of `CocoonConfig`. Otherwise the first check fails and the program exits.
- It uses a temporary SQLite database and an isolated configuration. It starts no network service and reads neither the operator configuration nor any player database.

## What it covers

### Day boundary

- At 03:59 server time the date still belongs to the previous day; a new day starts at 04:00.
- A reset hour of 0 and month boundaries.
- `ServerOption.DailyResetHour` outside the range is clamped to 0–23.

### Quest draw

- FNV-1a-64 + SplitMix64 + Fisher–Yates, so the result does not depend on the runtime.
- The same (uid, date, nonce) draws the same 8 quests; the order of the pool does not matter; incrementing the nonce draws a different set.
- Deleted `DailyQuest` rows do not enter the pool. QuestType 5 is no longer accepted automatically by its condition.

### Activity points and tiers

- Claiming a daily training reward grants item 23 and adds to the day's activity; `GetItem(23)` returns the current value.
- Unknown tier: 4101; repeated claim: 4102; not enough activity: 4103.
- Claim-all goes through the tiers in ascending order and merges the rewards; a repeated claim-all grants nothing.
- `TakeQuestOptionalReward` rejects a non-zero optional reward id (an unrelated reward id or a raw item id) with `RetReqParaInvalid`, grants nothing and leaves the quest finished; with id 0 it pays only the quest's `RewardID`, once.

### Day rollover

- A new period clears activity points and claimed tiers.
- Quests not drawn for the previous day are closed and their unclaimed rewards are forfeited. Quests drawn for the new day, including ones claimed earlier, are reset to in progress with progress 0.
- A nonce redraw, equivalent to `/daily reset`.
- `FinishQuest` quests (2100156/2100159) reactivate their child quests when drawn, so yesterday's completion does not satisfy them directly.

### Event-driven completion conditions

- A heartbeat (empty parameters) and unrelated parameters advance no event-driven quest.
- `AnyRelicLevelUp`, `AnyEquipmentLevelUp`, `AnyAvatarLevelUp`, `RogueFinishCnt`, `ResolveRelic`, `AnyItemComposeCount` and `ExpeditionSetOrTakeReward` advance only on their own events.
- `ChallengeFinishCnt` filters by challenge type.

### Calyxes and other domains

- A lost battle and a Stagnant Shadow battle (only `CocoonWave`, no `CocoonId`) do not count.
- A Golden Calyx completes `CocoonFinish` and `FarmTypeCocoonFinish [6,7,8]`; a Crimson Calyx completes `[5]`; a Cavern of Corrosion completes `AnyCocoonFinish [3]`.

### Daily missions

- `FinishTodayDailyMissionCnt` is reached after completing daily missions; the same mission counts once per period. At rollover the daily count resets and the cumulative count is kept.
- For players with the mission system enabled: no missions are issued before the unlock mission is met; a forced issue fills up to `DailyMissionCount.DailyCount`; issuing again on the same day adds nothing. Completed missions count, and after rollover completed missions become acceptable again and are reissued.

### Storage

- Daily training data is written to its own `DailyActive` table.

## Expected result

The run ends with `Daily active regression checks: <n>, failures: 0` and exits with code 0. Failed checks are listed under that line.

## Known gaps

- This is not a client test. The panel, claim animation, quest tracking, and the values the client reads from `DailyActiveInfoNotify` and `GetDailyActiveInfoScRsp` need verification on a real client.
- Conditions that are wired to counting events: `UseMazeSkill` (casting a technique), `PropDestruct` (destructible objects), `WinBattleWithAssistAvatarCnt` (winning with a support character), `RogueExploreRoomFinishCnt` (Simulated Universe rooms). `AnyFarmElementFinish` counts only Stagnant Shadow wins (Calyxes excluded). `UseItem` counts the quantity actually consumed (spending Trailblaze Power counts as item 11).
- Not implemented, so these quests cannot be completed once drawn: `ExcellentEnterBattleAndWin`, `ItemCompose`, `TakePictureCnt`, `UseItemWithType` and similar conditions.
- The Currency Wars child quest of 2100156 (`GridFightFinishGamePlay`) is not implemented either; 2100156 can only be completed through its Divergent Universe child quest.
