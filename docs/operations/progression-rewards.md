# Progression rewards

English | [简体中文](progression-rewards.zh-CN.md)

The server can pay bonus items when a player finishes a main mission or a sub-mission, or wins a battle. The bonus comes on top of the official rewards, which are unchanged. The setting lives in `Config.json` under `ServerOption.ProgressionRewards` and can be changed from the admin console (`rewards` group in Settings) without a restart. It is off by default.

## Configuration (`ServerOption.ProgressionRewards`)

| Key | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `false` | Master switch. When off, nothing is paid. |
| `MainMission` | 180 Stellar Jade, 10000 Credits, 1 Star Rail Pass | Paid for each finished main mission. |
| `SubMission` | 60 Stellar Jade, 2000 Credits | Paid for each finished sub-mission. |
| `BattleWin` | 20 Stellar Jade, 500 Credits | Paid for each won battle of an enabled kind. |
| `IncludeDailyMissions` | `false` | Daily main missions and their sub-missions repeat every day; they pay only when this is on. |
| `IncludeCommandFinishes` | `false` | Missions finished inside a command (`/mission pass`, `finish`, `finishmain`, `next`, `/unlockall rogue`, MUIP commands) pay only when this is on. |
| `BattleKinds` | only `Story` on | Which battle kinds pay `BattleWin`, see below. |

Reward lists are `{ "ItemId": n, "Count": n }` entries. In the admin console they are edited as one `itemId:count` per line; unknown item ids are rejected. Useful ids from the 4.7 item table: `1` Stellar Jade, `2` Credit, `101` Star Rail Pass (standard banners), `102` Star Rail Special Pass (limited banners).

## When a mission pays

A mission pays when it moves from accepted to finished, in the same step as its official reward. The bonus is added to the official reward notify, so the client shows both together. A finish call for a mission that is already finished pays nothing. Rogue tutorial missions that are being replayed pay nothing, like their official rewards. A main mission without sub-missions pays no main bonus: it has nothing to play, and the server closes such rows (display-only Gap rows, empty Branch rows) on its own.

A mission that is removed and accepted again (for example with `/mission rollback` or `/mission reaccept`) pays again when it is finished again. Those commands need GM permission.

## Battle kinds

A won battle is classified by the first rule that matches:

| Kind | Rule | Default |
| --- | --- | --- |
| `GmSpawn` | Every monster in the fight came from `/spawn` | off |
| `CurrencyWars` | Currency Wars (GridFight) battle | off |
| `Finality` | Finality battle | off |
| `Trial` | Trial activity stage, Battle College fight, or a trial-activity scene | off |
| `Rogue` | Divergent Universe, rogue magic, a running Simulated Universe run, or a rogue-like scene | off |
| `Challenge` | A running Forgotten Hall / Pure Fiction / Apocalyptic Shadow / tierce challenge, or a challenge scene | off |
| `Domain` | Calyx / Cavern / Echo of War (cocoon), Stagnant Shadow (farm element), any fight that costs Trailblaze Power, or a relic farm scene | off |
| `Raid` | A raid is running or the scene is a raid plane; this includes story instances that run as a raid | off |
| `Story` | Town, maze, train or town-room scene | on |
| `Other` | Anything else (event and minigame scenes) | off |

The bonus is added to the battle's drop list, so the result screen shows it. Lost or abandoned battles pay nothing.

Open-world enemies respawn, so with `Story` on, players can farm `BattleWin` by refighting them.
