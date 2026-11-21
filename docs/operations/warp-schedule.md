# Warp schedule

The server can serve limited warp banners from a pool of past banners instead of only the fixed list in `Config/Custom/Banners.json`. The setting lives in `Config.json` under `ServerOption.Warps` and can be changed from the admin console (Warps panel, or the `warps` group in Settings) without a restart.

## Modes

| Mode | Limited banners served | Notes |
| --- | --- | --- |
| `Static` (default) | The limited banners of `Banners.json`, with the windows written there | The behaviour before warp scheduling existed. |
| `Rotation` | One phase of the history pool per rotation period (one whole version with `PhaseLineup` `Version`), in historical order, wrapping after the newest | Banners get the current period as their window. |
| `Manual` | The admin-picked list in `ServerOption.Warps.Manual` | Each entry can have its own window and featured items. |

The permanent pools (`Newbie`, `Normal`) always come from `Banners.json`, in every mode. In `Rotation` and `Manual` the limited banners of `Banners.json` are not served. There is no cap on the number of limited banners served at once.

Switching to `Manual` while the list is empty seeds it with the banners that are running at that moment, so the switch itself changes nothing players see. Switching to `Rotation` without a start date starts the rotation today.

## Collab banners

The collaboration banners of the history pool (the four collab character banners and their four light cone banners, ids 5001–5004 and 6001–6004) are a group of their own, like the permanent pools. With `CollabBanners` on (the default) all of them are served in every mode, to every player, with the current rotation period as their window: they are not part of any phase lineup, the Story filter does not hide them, and a Manual list cannot hold them. With it off none are served. Collab rows in `Banners.json` are never served; the group comes from the history pool.

## Story filter

With `Story` on, each player is offered only the limited banners of versions whose story they have reached. It works on top of `Static` and `Rotation`; `Manual` ignores it, because the admin picked the list on purpose.

| Mode | With `Story` on |
| --- | --- |
| `Static` | Every unlocked history banner at once, with the current rotation period as its window. The limited banners of `Banners.json` are not served. Late in the story this is a long list: the client has not been tested with that many banners. |
| `Rotation` | Only the unlocked phases rotate, in historical order. The rotation starts at `RotationStartPhase` when that phase is unlocked, otherwise at the oldest phase. |
| `Manual` | No change. |

- **When a version unlocks.** `Config/Custom/WarpStoryGates.json` lists, for each version that added Trailblaze story, the main missions of its chapters. A player has reached a version once any of them is accepted or finished, so a version unlocks when its chapter starts. Version 1.0 is always unlocked.
- **Versions without new story** (1.1, 1.4–1.6, 2.4–2.6) unlock together with the story version before them: a player in 1.0's story sees the 1.0 and 1.1 banners.
- **Order follows the version, not mission ids.** 3.8's chapter ("Memories are the Prelude to Dreams") has lower mission ids than 3.6 and 3.7.
- **No story to read.** A player with the mission system off (server `EnableMission` false, or the per-account override) gets the `Banners.json` limited banners as written, in every mode. The same happens when the gate file is missing or empty.
- The list is computed per player on every warp request, and `DoGacha` checks the same list, so a locked banner cannot be pulled. The admin Warps page shows the server-wide schedule; it notes when the filter narrows it per player.

### Generated data

`scripts/generate-warp-data.py <resource dir>` writes two files into `Config/Custom` from a raw game-data tree with `ExcelOutput/` and `TextMap/` (for example `TurnBasedGameData`):

- `WarpStoryGates.json`: the main missions of every story chapter (`MainMission.json`, `Type` `Main`, grouped by `ChapterID`), grouped by version. The game data does not say which version shipped a chapter, so the script carries that table, taken from the Fandom wiki's [Trailblaze Mission change history](https://honkai-star-rail.fandom.com/wiki/Trailblaze_Mission/Change_History). A new version needs one new line there; the script stops when the data has a story chapter the table does not cover. The server has no chapter data of its own, which is why the mission ids are listed explicitly.
- `WarpItemNames.json`: character and light cone names in English and Simplified Chinese, for the admin Warps page (cards, edit dialog, search by name). The page falls back to the GM handbook when an id is not in the file.

## Configuration (`ServerOption.Warps`)

| Key | Default | Meaning |
| --- | --- | --- |
| `Mode` | `"Static"` | `Static`, `Rotation` or `Manual` (case-insensitive). An unknown value behaves as `Static`. |
| `RotationDays` | `7` | Length of one rotation period in days (admin settings accept 1..365). Manual entries without an end also roll over with it. |
| `RotationStart` | `""` | `yyyy-MM-dd`; period 0 starts at server-local midnight of that day. Empty = 1970-01-01 UTC. |
| `RotationStartPhase` | `""` | Phase key (for example `"4.7-1"`) served in period 0. Empty or unknown = the oldest phase. |
| `PhaseLineup` | `"Started"` | Which banners make up a phase in `Rotation` and Load phase: `Started` = only the banners that opened in it; `Overlap` = also every earlier banner still running when it opened (the live lineup); `Version` = every banner that opened in either phase of its version, and the rotation steps one version per period. An unknown value behaves as `Started`. |
| `CollabBanners` | `true` | Serve the collab banners as their own group in every mode (see above); `false` = never. |
| `Story` | `false` | Story filter, see above. |
| `Manual` | `[]` | Manual-mode entries, see below. |

Manual entry fields:

| Field | Meaning |
| --- | --- |
| `GachaId` | Id of a banner in the eligible history pool. Each id may appear once. |
| `BeginTime`, `EndTime` | Unix seconds. `0` = take that bound from the current rotation period, so the banner never runs out. When both are set, begin must be before end. |
| `RateUpItems5`, `RateUpItems4` | `null` = the history entry's featured items. Otherwise every id must exist in the game data, and `RateUpItems5` cannot be empty. The featured panel is rebuilt from the overrides. |
| `PairedGachaId` | Light cone banner shown beside this character banner in the admin panel; `0` = none. UI hint only, it does not change what is served. |

The admin endpoints validate the whole list and replace it in one assignment; an invalid list is rejected and leaves the running one unchanged. When editing `Config.json` by hand, run the same checks yourself or use the admin API.

## History pool

`Config/Custom/Banners.history.json` holds past limited banners (the shipped file has 300, versions 1.0 to 4.7). Each row is a `Banners.json` banner plus `version` and `phase`, the version phase the banner started in.

- **Eligibility.** A row is used only when it is a limited type (`AvatarUp`, `WeaponUp`, `CollaborationAvatarUp`, `CollaborationWeaponUp`), has at least one featured 5★, and every id in `rateUpItems5`, `rateUpItems4` and `prizeItems` exists in the loaded item table (`ItemConfigData`). A banner featuring content the client data lacks would pay out the fallback item instead, so such rows are left out and listed as `ineligible` by the admin API.
- **Phases.** Rows are grouped by `version` + `phase`. A phase starts at the earliest `beginTime` of its rows (eligible or not). With `PhaseLineup` `Started` its lineup is the eligible banners tagged with that phase. With `Overlap` it also includes every eligible banner whose historical window covers the phase start, so a banner that ran through both phases of a version appears in every later phase it overlapped, as on the live servers. With `Version` the lineup is the `Started` banners of every phase of the version; the rotation, the restart list and Load phase then use one step per version (its first phase), and a `RotationStartPhase` such as `4.7-2` starts at version 4.7. Collab banners are never part of a phase lineup. Phases are ordered by start time; with the shipped file and full item data there are 65.
- **Fresh windows.** History windows are never served as-is. Rotation gives every banner of the current phase the current period (`[periodBegin, periodEnd]`, inclusive). Manual entries use their explicit times or the current period.
- **Fake server time.** Windows are computed on the real clock and then shifted by the difference between the player's server clock and the real clock, so an account on a fake date (global `ServerTime` or per-account) still sees the banners as running.
- **Pity** is kept per pool family (character event, light cone event, the two collaboration families, standard, newbie), not per banner. It carries across all banners of the same type, including when the lineup changes.

## Admin API

All routes require an admin session (`Authorization: Bearer <token>` or `X-Admin-Session`) and answer `{ "retcode": 0, "message": "OK", "data": ... }`; errors answer HTTP 400 with the reason in `message` (401 without a session).

| Method and path | Body | Result |
| --- | --- | --- |
| `GET /admin/api/warps` | — | Mode, `now`, rotation state (`days`, `start`, `start_phase`, `period_begin`, `period_end`, `current_phase`, `next_phase`), `permanent` (`Banners.json` Newbie/Normal), `scheduled` (limited banners of the current mode with served windows, including manual entries not yet open or already closed; server-wide, without the Story filter and the collab group), raw `manual` list, `pool_size`, `phase_count`, `ineligible` ids, the filter settings `story`, `story_active` (on and not `Manual`), `phase_lineup`, and `collab_banners` with the `collab` group (served windows when on; history rows, not running, when off). |
| `GET /admin/api/warps/pool` | — | `phases` (the rotation steps: every phase, or one per version, keyed by its first phase, with `PhaseLineup` `Version`; `key`, `version`, `phase`, `start_time`, `banner_ids` under the current `PhaseLineup` setting), the eligible `banners` with their historical windows, and `names` (`WarpItemNames.json`: `{ "en": { "1306": "Sparkle", ... }, "zh-CN": { ... } }`). |
| `PUT /admin/api/warps/manual` | `{ "entries": [{ "gacha_id", "begin_time"?, "end_time"?, "rate_up5"?, "rate_up4"?, "paired_gacha_id"? }] }` | Validates, replaces the manual list, switches to `Manual`, saves `Config.json`. An empty list is allowed (no limited banners). |
| `POST /admin/api/warps/mode` | `{ "mode": "Static" \| "Rotation" \| "Manual" }` | Switches mode (seeding as described above) and saves. |
| `POST /admin/api/warps/rotation` | `{ "phase": "4.7-1" }` | Restarts the rotation today from that phase, switches to `Rotation`, saves. |

Banner objects carry `id`, `gacha_type`, `version`, `phase`, `rate_up5`, `rate_up4`, `begin_time`, `end_time`, `running` (window contains `now`) and `default_pair_id` (the light cone banner `id + 1000` that ran beside a character banner, or 0).

The Settings page exposes `Warps.Mode`, `Warps.RotationDays`, `Warps.RotationStart`, `Warps.RotationStartPhase`, `Warps.Story`, `Warps.PhaseLineup` and `Warps.CollabBanners`; it rejects an unknown mode or lineup, a malformed date and a phase key that is not in the pool. The Warps page also has the Story and Collab banners switches. A manual list with a collab banner is rejected (HTTP 400).

## Reloading and client behaviour

- `/reload banner` re-reads `Banners.json`, `Banners.history.json`, `WarpStoryGates.json` and `WarpItemNames.json`; the pool is rebuilt on next use. A resource reload that replaces the item table also re-evaluates eligibility.
- Schedule changes take effect for the next warp request. Players see them the next time they open the warp screen or log in again (not verified on a real client).
- Not tested: how the client UI copes with many limited banners at once.
