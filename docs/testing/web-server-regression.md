# Web admin regression

English | [简体中文](web-server-regression.zh-CN.md)

`WebServerRegression` checks the web admin, MUIP, command target isolation and asynchronous command output. Run it after changing `WebServer/`, the admin or player console pages, MUIP or how commands reach a target.

Project: [`scripts/WebServerRegression/`](../../scripts/WebServerRegression/)

## How to run

From the repository root:

```powershell
dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj
```

To check the pages in a browser, add `--serve`:

```powershell
dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj -- --serve
```

Open `http://127.0.0.1:21000/admin/index.html` and sign in with the test key `local-web-regression-only`. Stop it with Ctrl+C when you are done. This mode only starts a test admin on the loopback address; do not use it to verify configuration or database writes. The warp page loads the repository's `Banners.history.json` and `WarpItemNames.json` and adds placeholder rows for the items the banners reference, so you can browse named banners, search by name and toggle the story filter.

The account page's "Create at story point" and "Jump to story point" need game data. Add `--with-resources` (`-- --serve --with-resources`) to load `Config/Resource.bin` read-only; story replay writes to this run's temporary database, and snapshots go to `Snapshots/` under the temporary database directory. Without it the story catalog is empty and creation returns 400.

## Prerequisites

None beyond the .NET SDK. The regression uses its own in-memory data and a temporary directory, and does not load the project's `Config.json` or any player database. Simulated connections and commands are registered only inside the test process.

## What it covers

### Commands and admin

- Commands without a target, target isolation, asynchronous output and exceptions, concurrent requests.
- `/language` answers the admin console that it is player-only; the player self-service console can pin its own language and replies in the new language.
- `/json export` from a User-role player refuses traversal and absolute names and keeps a default name built from the player name inside `Config/Json`; `/json <path>` refuses to import a file outside `Config/Json`.
- Login protection and the in-memory data of the maintenance endpoints.
- Every `/admin/api` endpoint in the routing table, apart from login, logout and the session check, refuses a request without a session with the same 401 body; the list is read from the running application, so new endpoints are covered automatically. The token is accepted from a Bearer header, `X-Admin-Session` or the `token` query parameter, and rotating the admin key keeps the caller's own session.

### MUIP and gateway

- MUIP GET and POST; only one command handler can be registered.
- MUIP server information lists the online players; player information reports an online player whose login initialisation has not run as exploring, and an offline player as offline.
- `query_gateway` hands out the Divergent Universe BuildRef address and request headers; the two exact BuildRef JSON routes.

### Credential revocation and MUIP session lifecycle

- `/sr-tools` rejects an empty or wrong password whether RSA login decryption is on or off; a correct password still goes on to the normal online-status check.
- Player console tokens are bound to the password version at issue time. After an admin password change or an e-mail password reset the old token stops working, and a new login still works. This needs no new database column.
- MUIP sessions are bound to the admin key version at creation time. After a configuration change, the authentication, command, server-info and player-info endpoints all reject old sessions. Rotating the key from the admin page also clears MUIP admin sessions.
- Anonymous MUIP session creation is refused at capacity. Expired, abandoned sessions are cleaned up on creation, and the service also cleans them up every minute. The total limit is 1024, with at most 32 unexpired sessions per HTTP source. The admin page and the player console run each command on a per-request session that never enters the MUIP session table, so they neither use the creation endpoint nor count toward its limits.
- A wrong key at `/muip/auth_admin` deletes that MUIP session and counts against the same per-client failure counter as the admin page login. After 8 wrong keys from one client the correct key is refused (code 3) for that client, on both MUIP and the admin page, while other clients are unaffected.
- Root `/sr-tools` import no longer accepts a UID alone, even with RSA decryption off; old accounts without a password must set one first.
- SDK `createOrder` only credits the online player whose combo or dispatch token is presented (in `x-rpc-combo_token` or the request): a missing, forged or other account's token, or no uid at all, leaves the player's supply pass untouched; there is no fallback to the only online player.
- A passwordless account is refused by console login, session, catalog, command execution and self-reset, and by `/sr-tools` and `/sr-tools-export`.
- `/sr-tools-export` rejects a missing or wrong password, accepts credentials in the `POST` body, and answers the old query-string `GET` with 405.
- Once wrong passwords on the console, `/sr-tools` or `/sr-tools-export` trigger the login rate limit, the correct password is refused on all three. An IP block and an account ban are also enforced on both sr-tools routes.
- `DefaultPermissions` defaults to empty, a default-role account has no `*`, `command.*`, `command.account` or `command.others`, and the startup warning flags `*` and high-privilege entries but not User-role permissions. The fixture no longer clears `DefaultPermissions` itself, so the console permission checks run under the default configuration.

### Private chat

- A private message is delivered and stored only between mutual friends: messages to a non-friend, to oneself, to a player one has blocked, or from a sender the target has blocked are rejected and stored on neither side.
- A request naming more than `FriendManager.MaxMessageTargets` recipients is rejected without delivering anything; a mixed request still delivers to friends and reports the first rejection.
- Each stored conversation, including an offline friend's, keeps at most `FriendManager.MaxConversationHistory` messages.

### Warp schedule

`WarpChecks.cs` runs after the admin login and covers `/admin/api/warps*` and the `warps` group of the settings page (behaviour: [Warp schedule](../operations/warp-schedule.md)). The cases write synthetic data in process: a few items in `ItemConfigData`, a `Banners.json` with standard banner 1001 and limited banner 2139, and a history pool across three phases (one banner spans two phases, and another banner's 5★ is missing from the item table). Checks:

- Warp endpoints return 401 without a login. In `Static` mode `scheduled` is the limited banners of `Banners.json`, and the player-visible list contains both the standard and limited banners.
- The banner library is grouped by phase. With the default `PhaseLineup` of `Started`, a banner spanning two phases appears only in the phase where it opened; with `Overlap` it appears in the `banner_ids` of both phases; with `Version` each version is listed once (keyed by its first phase), and `banner_ids` holds every banner open in either phase of that version. A banner with missing items is listed under `ineligible` and left out of the library; rows carry the historical times, version phase and default light cone banner pairing.
- Switching to `Rotation` rotates from today starting at the earliest phase. `scheduled` is the current phase lineup, its start and end equal `rotation.period_begin/period_end`, and all are active; limited banner 2139 from `Banners.json` is no longer served. `POST /admin/api/warps/rotation` switches the current phase; an unknown phase or mode returns 400.
- The settings page rejects an unknown `Warps.RotationStartPhase`, a malformed `Warps.RotationStart`, an unknown `Warps.Mode` and an out-of-range `Warps.RotationDays`, leaving the configuration unchanged; a known phase can be saved and takes effect immediately. The `warps` group has 8 fields.
- `Warps.Story`, `Warps.StoryByChapter`, `Warps.PhaseLineup` (case-insensitive, saved by enum name) and `Warps.CollabBanners` can be saved through the settings endpoint and read back from `fields`; `/admin/api/warps` then returns `story`, `story_by_chapter`, `story_active`, `phase_lineup` and `collab_banners`. An unknown `Warps.PhaseLineup` is rejected and the configuration is unchanged; the defaults are then restored (`Warps.StoryByChapter` back to false).
- Switching to `Manual` seeds the manual list from the current lineup (rolling time window, default light cone pairing). An unknown, ineligible or duplicate banner, an unknown item, an empty 5★ override, a start not before the end, a wrong pairing and missing `entries` all return 400 and leave the original list object unchanged.
- After a valid submission, 5★ overrides and explicit time windows show in `scheduled`; entries without times follow the rotation period, and entries that have not started are listed but not active. An empty list can be saved.
- `WarpSchedule.GetActiveBanners(realNow, playerNow)`, with the player clock 200 days behind, shifts the limited banner windows onto the player clock (the window contains the player time, not the real time), still includes standard banner 1001 and excludes 2139.
- It reads the repository's `Config/Custom/Banners.history.json`: 300 banners, all eligible once every referenced item is added, 65 phases in total. The `Overlap` lineup of `4.7-2` (`WarpPhase.Overlapping`) contains 2142, 3142, 2146, 2147, 2148, 3146, 3147 and 3148. Collab banners 5001–5004 / 6001–6004 form their own group (`WarpPool.Collab`) and belong to no phase. The library endpoint returns the same counts.
- It also loads the repository's `Config/Custom/WarpItemNames.json`: in the library endpoint's `names`, `en` maps 1306 to Sparkle and 1501 to Sparxie, and a `zh-CN` table exists. The endpoint's `banner_ids` for `4.7-2` are exactly 2146, 2147, 2148, 3146, 3147 and 3148 under `Started`; under `Overlap` they include 2142 and no collab banner.
- Collab banners: with `CollabBanners` on, `/admin/api/warps` returns `collab_banners` true and `collab` is exactly 5001–5004 and 6001–6004, all active, while `scheduled` excludes them. After turning it off through the settings endpoint, `collab_banners` is false and `collab` still lists the 8 banners, none active. A manual list containing collab banner 5001 returns 400 and the original list object is unchanged. The original settings are restored afterwards.

At the end it restores the `ServerOption.Warps` defaults and the original banner configuration and removes the temporary items; `Config.json` is only written to the temporary directory.

### Account clones, save snapshots and story checkpoints

Behaviour: [Story checkpoints, account clones and save snapshots](../operations/story-checkpoints.md).

- `SnapshotChecks.cs`: a clone gets a new account with the requested credentials and correct save fields and nickname, shares no object with the source account and copies no friends; after changing the clone's currency and missions, the source account is unchanged both in memory and in SQLite read directly. Cloning a missing account returns 404; a username clash or a source account without a save returns 400 and adds no account. Snapshots get an id from their name (a duplicate name appends `-2`), record metadata and are written to `Snapshots/` under the database directory; listing, creating a test account from a snapshot (the generated password can log in) and restoring onto an existing account (memory and SQLite both replaced, the account kept) all work. Invalid ids such as `..%2F..` and `a.b`, and path traversal, return 400; a missing snapshot returns 404; a corrupt snapshot file returns 400 and leaves no account; unknown tables are ignored; a column that cannot be deserialised returns 400 and leaves the target save unchanged; after deletion the file is gone, and deleting again returns 404.
- `StoryChecks.cs` (two synthetic main missions; this regression does not load the resource cache): 401 without a login; the story catalog lists main missions, chapters, floors and sub-missions in version and gate order, and only lists presets whose missions exist; an unknown main mission, a sub-mission outside that main mission, and an unknown stop position, gender or preset return 400 and add no account; when replay fails for missing resources no account is left behind, and an existing account gets its original save back; a missing account returns 404. Successful replay of real story content (including the HTTP endpoints) is verified in the login and scene regression.
- `EditorChecks.cs` (player editor, behaviour: [Player editor](../operations/player-editor.md); this regression does not load the resource cache): the summary reports offline with no undo point; an edit with a stale `version` returns 409 and changes nothing; without resources, an edit whose save fails to load returns 500, restores the original save and leaves no undo point. Raw data lists the save tables (excluding Account), and the Player table round-trips losslessly; editing JSON replaces only that table (Uid pinned to the target account, other tables kept, SQLite in sync), with 409 for a stale version, 400 for malformed input and 404 for an unknown table. Undo restores the save from before the edit, and undoing again redoes it; while another tool holds the lease, both edit and undo return 409; an unknown account and a missing undo point return 404. Delete on logout: an account can be flagged through the endpoint; a session replaced by a new login, a session stopped by a tool lease and a normal account's logout delete nothing; a flagged account is deleted with its save on logout (memory and SQLite); a clone can be created directly as a delete-on-logout account (the SQLite column is written). Edits that need to load the save (profile, avatars, items, missions) are verified in the login and scene regression.

### Progression reward settings

`ProgressionRewardSettingsChecks.cs` checks the `rewards` group of the settings page through `PUT /admin/api/settings` (behaviour: [Progression rewards](../operations/progression-rewards.md)): the group has 16 fields, is off by default, main mission rewards are `1:180`, `2:10000` and `101:1`, and only story battles are ticked. Unknown items, missing amounts and non-positive amounts are rejected and leave the original list object unchanged; the switch, the line-edited reward list (accepting `\r\n` and space separators) and the battle types can be saved together. At the end it restores `ServerOption.ProgressionRewards` and removes the temporary items.

These cases use only temporary accounts, a temporary database and loopback HTTP, and never touch real player saves.

## Expected result

The run ends with:

```text
Regression failures: 0
```

The regression prints how long the maintenance endpoints take. The exception test prints one expected error log.
