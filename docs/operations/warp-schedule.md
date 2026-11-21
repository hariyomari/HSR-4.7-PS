# Warp schedule

The server can serve limited warp banners from a pool of past banners instead of only the fixed list in `Config/Custom/Banners.json`. The setting lives in `Config.json` under `ServerOption.Warps` and can be changed from the admin console (Warps panel, or the `warps` group in Settings) without a restart.

## Modes

| Mode | Limited banners served | Notes |
| --- | --- | --- |
| `Static` (default) | The limited banners of `Banners.json`, with the windows written there | The behaviour before warp scheduling existed. |
| `Rotation` | One phase of the history pool per rotation period, in historical order, wrapping after the newest phase | Banners get the current period as their window. |
| `Manual` | The admin-picked list in `ServerOption.Warps.Manual` | Each entry can have its own window and featured items. |

The permanent pools (`Newbie`, `Normal`) always come from `Banners.json`, in every mode. In `Rotation` and `Manual` the limited banners of `Banners.json` are not served. There is no cap on the number of limited banners served at once.

Switching to `Manual` while the list is empty seeds it with the banners that are running at that moment, so the switch itself changes nothing players see. Switching to `Rotation` without a start date starts the rotation today.

## Configuration (`ServerOption.Warps`)

| Key | Default | Meaning |
| --- | --- | --- |
| `Mode` | `"Static"` | `Static`, `Rotation` or `Manual` (case-insensitive). An unknown value behaves as `Static`. |
| `RotationDays` | `7` | Length of one rotation period in days (admin settings accept 1..365). Manual entries without an end also roll over with it. |
| `RotationStart` | `""` | `yyyy-MM-dd`; period 0 starts at server-local midnight of that day. Empty = 1970-01-01 UTC. |
| `RotationStartPhase` | `""` | Phase key (for example `"4.7-1"`) served in period 0. Empty or unknown = the oldest phase. |
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
- **Phases.** Rows are grouped by `version` + `phase`. A phase starts at the earliest `beginTime` of its rows (eligible or not). Its lineup is the eligible banners tagged with that phase, plus every eligible banner whose historical window covers the phase start. A banner that ran through both phases of a version, and the long-running collaboration pools, therefore appear in every later phase they overlapped, as they did on the live servers. Phases are ordered by start time; with the shipped file and full item data there are 65.
- **Fresh windows.** History windows are never served as-is. Rotation gives every banner of the current phase the current period (`[periodBegin, periodEnd]`, inclusive). Manual entries use their explicit times or the current period.
- **Fake server time.** Windows are computed on the real clock and then shifted by the difference between the player's server clock and the real clock, so an account on a fake date (global `ServerTime` or per-account) still sees the banners as running.
- **Pity** is kept per pool family (character event, light cone event, the two collaboration families, standard, newbie), not per banner. It carries across all banners of the same type, including when the lineup changes.

## Admin API

All routes require an admin session (`Authorization: Bearer <token>` or `X-Admin-Session`) and answer `{ "retcode": 0, "message": "OK", "data": ... }`; errors answer HTTP 400 with the reason in `message` (401 without a session).

| Method and path | Body | Result |
| --- | --- | --- |
| `GET /admin/api/warps` | — | Mode, `now`, rotation state (`days`, `start`, `start_phase`, `period_begin`, `period_end`, `current_phase`, `next_phase`), `permanent` (`Banners.json` Newbie/Normal), `scheduled` (limited banners of the current mode with served windows, including manual entries not yet open or already closed), raw `manual` list, `pool_size`, `phase_count`, `ineligible` ids. |
| `GET /admin/api/warps/pool` | — | `phases` (`key`, `version`, `phase`, `start_time`, `banner_ids`) and the eligible `banners` with their historical windows. |
| `PUT /admin/api/warps/manual` | `{ "entries": [{ "gacha_id", "begin_time"?, "end_time"?, "rate_up5"?, "rate_up4"?, "paired_gacha_id"? }] }` | Validates, replaces the manual list, switches to `Manual`, saves `Config.json`. An empty list is allowed (no limited banners). |
| `POST /admin/api/warps/mode` | `{ "mode": "Static" \| "Rotation" \| "Manual" }` | Switches mode (seeding as described above) and saves. |
| `POST /admin/api/warps/rotation` | `{ "phase": "4.7-1" }` | Restarts the rotation today from that phase, switches to `Rotation`, saves. |

Banner objects carry `id`, `gacha_type`, `version`, `phase`, `rate_up5`, `rate_up4`, `begin_time`, `end_time`, `running` (window contains `now`) and `default_pair_id` (the light cone banner `id + 1000` that ran beside a character banner, or 0).

The Settings page exposes `Warps.Mode`, `Warps.RotationDays`, `Warps.RotationStart` and `Warps.RotationStartPhase`; it rejects an unknown mode, a malformed date and a phase key that is not in the pool.

## Reloading and client behaviour

- `/reload banner` re-reads both `Banners.json` and `Banners.history.json`; the pool is rebuilt on next use. A resource reload that replaces the item table also re-evaluates eligibility.
- Schedule changes take effect for the next warp request. Players see them the next time they open the warp screen or log in again (not verified on a real client).
- Not tested: how the client UI copes with many limited banners at once.
