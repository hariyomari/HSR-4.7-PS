# Player editor

English | [简体中文](player-editor.zh-CN.md)

The admin console's **Player editor** page (`/admin/index.html#/editor/<uid>`, sidebar group *Players*) edits one account's gameplay save section by section: profile and currencies, characters, inventory, teams, story and world, and the raw save tables. The Accounts page links to it from the profile and *Player data* tabs. Test-account creation stays on the Accounts page.

## How a change is applied

Every change runs **offline** through `GameServer/Game/Player/OfflinePlayerSession.cs`:

1. The uid is taken with a lease. While it is held, `PlayerGetTokenCsReq` refuses logins with `RetSystemBusy`, and a second tool (another editor change, a snapshot restore, a story provision) gets a 409.
2. Every session of the uid is stopped (an online player is disconnected; the console says so).
3. Edits that name a section `version` are checked against the stored save. A save changed since the page loaded it (typically by the player while online) is a 409 and nothing changes; the page reloads the section.
4. The stored save is loaded into a session-less `PlayerInstance`, as a login does, and the change runs through the game's own managers: `AvatarManager.AddAvatar`, `InventoryManager.AddItem`, `MissionManager` (finish actions and narrative hooks run), `TryPlaceAtEntrance`, and so on. Then the save is written.
5. If the change throws, the previous save is put back. If it succeeds, the save from before it is kept as the uid's **undo point**.

Values the client cannot show are refused with a 400: levels outside 1–80 (characters, light cones) or the player level table, an ascension the level does not allow, eidolons outside 0–6, superimposition outside 1–5, more than 100 light cone/relic copies in one grant, unknown ids, a character twice in a team, an empty current team, removing the Trailblazer.

## Undo

The undo point is one file per uid, `Snapshots/undo/<uid>.json` next to the database (the snapshot format; it is not listed among the snapshots). Every editor change, snapshot restore, clone onto an existing uid and story provision of an existing save replaces it. *Undo last change* (`POST .../editor/undo`) puts that save back and makes the replaced save the new undo point, so a second undo redoes the change. Deleting the account deletes its undo file.

## Sections and API

All routes are under `/admin/api/players/{uid}/editor` and need an admin session.

| Section | Read | Change |
| --- | --- | --- |
| Summary | `GET` → `exists`, `online`, `busy` (a tool holds the uid), `mission_enabled`, `undo` (`label`, `created_at`), level caps, `delete_on_logout` | `POST /undo` |
| Profile | `GET /profile` → nickname, signature, head icon, level, EXP, Equilibrium, credits, Stellar Jade, Oneiric Shards, Trailblaze Power and reserve, month card expiry, gender (`gender_set` is false while `AvatarManager.IsHeroGenderPending`), `version` | `PUT /profile` (all fields + `version`; `gender`: `keep`/`man`/`woman`). Raising the level runs the level-up mission acceptance. Gender switches through the game's `SetHeroGender`: the path is kept and the hero's path id paired; teams store the base id 8001, so they need no change. Refused while the story's gender prompt is still ahead. |
| Characters | `GET /avatars` → each owned character with its paths, level, ascension, eidolon, worn light cone and relics (`relics`: slot, unique id, item id), `traces_maxed`, plus `version` | `POST /avatars {avatar_id}`, `PUT /avatars/{id} {level, promotion?, rank, max_traces, version}` (eidolon applies to every path; no `promotion` picks the lowest the level allows), `PUT /avatars/{id}/gear {light_cone_unique_id?, relics: {"1".."6": unique_id}}` (current path; 0 takes off, omitted keeps; a relic must fit its slot; gear worn by someone else is swapped as in the game), `DELETE /avatars/{id}` (leaves every team, gear unequipped) |
| Inventory | `GET /inventory` → materials, light cones, relics | `POST /inventory/grant {item_id, count, level?, rank?, promotion?}` (relic stats are rolled as in a drop), `PUT /inventory/items/{item_id} {count}` (exact count, 0 removes), `PUT /inventory/light-cones/{unique_id} {level, promotion?, rank}`, `POST /inventory/remove {unique_ids}` (taken off their wearer) |
| Teams | `GET /lineups` → teams 1–9 with slots, the current one, an active story/raid team, `version` | `PUT /lineups/{index} {slots: [{base_avatar_id, special_avatar_id}], make_current, version}`. Story trials already in a team can stay or go; new ones cannot be added. |
| Story | `GET /story` → mission system, tracked main, running mains with their running subs, finished mains, position, teleport and tutorial counts | `POST /story/missions {action, mission_id?, count?, strict?}` with `advance`, `rollback`, `repair`, `finish_sub`, `finish_main`, `accept_main`, `reaccept_main`, `remove_main`, `track`, `enable`, `disable` (the `/mission` command's operations); `POST /story/teleports {mode: reached_planes|all}`; `POST /story/tutorials`; `PUT /position {entry_id}` |
| Raw data | `GET /raw` → the gameplay tables and which the save holds; `GET /raw/{table}` → `json`, `editable`, `version` | `PUT /raw/{table} {json, version}` replaces that one table and keeps the rest of the save. |

`GET /admin/api/editor/catalog` serves the pickers: playable characters (`id`, `base_id`, `rarity`), light cone, relic and other item ids, and map entrances. Names come from the GM handbook in the browser; without `Config/Handbook` the pickers show ids.

### Story points

The Story section opens the story dialog with two modes:

- **Advance** (`keep_save: true` on `POST /admin/api/accounts/{uid}/provision-story`): the provisioner loads the stored save instead of wiping it and plays only the missions still open on the way to the target. Characters, items and finished missions stay. A save already past the point is a 400 and is left as it was.
- **Jump**: the existing wipe-and-replay ([story checkpoints](story-checkpoints.md)).

Both keep the save from before as the undo point.

### Raw tables

The JSON is the table's row as Newtonsoft serialises it. A table is editable only when its JSON reads back into exactly the stored row (compared column by column after the database round trip); others are shown read-only. On the 4.7 cache all 35 tables of a provisioned save round-trip. Bad values can still stop a login; use undo.

### Supply pass expiry

The profile's *month card expiry* is the Express Supply Pass's stored end time, `MonthCardExpireTime` in Unix seconds (0: never bought). Reading it, here, in the shop or at login, never starts or renews a pass: only an authenticated recharge-shop purchase extends it, and the editor (profile or raw table) stores exactly the value entered. The pass is active while that time is after the account's server time, `/faketime` included. A purchase adds 30 days to a running pass, even one whose expiry is not on a daily reset; a new or lapsed pass starts at the reset that opened the current window (`ServerOption.DailyResetHour`). Another purchase is refused once 179 or more days are left.

The daily 90 Stellar Jade are paid once per reset window, and the save keeps only the last window paid (`LastMonthCardRewardDate`):

- A last claim saved by an older build is a calendar date (`yyyyMMdd`, rolled over at local midnight), not a reset window. On the cutover day, a claim made between midnight and the reset can count for the wrong window once, so one daily reward is paid twice or not at all.
- Moving an account's time backwards with `/faketime` reaches windows other than the last one paid, and they pay again.

This is simulated fulfillment, not an exactly-once payment ledger.

## Delete-on-logout test accounts

An account flagged `DeleteOnLogout` is deleted with its save (tombstoned, like an admin deletion) when its player's session ends: a logoff, a disconnect or a kick. A session replaced by a new login of the same uid, and a session stopped by an editor/provision/restore lease, do not count.

- Set it when creating a test account (`delete_on_logout: true` on clone, snapshot spawn and story provision; the *Delete account after logoff* switch in the dialogs).
- Toggle it on an existing account on the *Danger* tab (`PUT /admin/api/accounts/{uid}/delete-on-logout {enabled}`).
- The flag is a new `Account` column added by the schema migration; existing accounts read it as off.

## Not covered by server regressions

- Client behaviour after an edit (character screens, inventory, team UI, the map after a position change) needs an in-game check.
- A delete-on-logout account deleted through a real client disconnect: the session-end logic is checked directly (`EditorChecks`), not through a live KCP session.

The page's write actions were each driven once in a browser against the `--serve --with-resources` fixture (profile with gender, character add/edit/gear/remove, grants, material count, light cone edit, gear removal, teams, mission buttons, teleports, tutorials, position, raw save, undo, the Danger-tab switch); that is a page check, not a client check.
