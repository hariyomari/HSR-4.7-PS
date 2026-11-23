# Story checkpoints, account clones and save snapshots

The admin console (Accounts panel) can build a test account that stands at any point of the Trailblaze story. It can also copy an account's whole save to a new account, or keep the save as a named snapshot. All of these replace **gameplay data only**: the account row (username, password, role, tokens, email, bans) is never copied or overwritten.

## Account clone

`POST /admin/api/accounts/{uid}/clone` creates a new account and gives it a deep copy of `{uid}`'s save.

- Body: `{username?, password?, email?, uid?, role?, nickname?}`. An empty username becomes `<source>_clone_<hex>`. An empty password is generated and returned once as `generated_password`. An empty email becomes `<username>@local.test`. `nickname` renames the copied Trailblazer.
- An online source is flushed first. The copy goes through the same row round trip as a database save and reload (`EntityStore.DeepCopy`), so the clone shares no object with the source. All rows are written in one SQLite/MySQL transaction.
- `Friend` and `assist_reward_data` are not copied, because they name other players' uids. The clone starts with no friends and no pending support rewards.
- If the copy fails, the new account is retired again (deleted with a tombstone).

## Save snapshots

Snapshots are files under `Snapshots/` next to the database (default `Config/Database/Snapshots/<id>.json`, already git-ignored). They are player save data: back them up and protect them like the database.

| Route | Purpose |
| --- | --- |
| `GET /admin/api/snapshots` | List the snapshot metadata: `id`, `name`, `description`, `created_at`, `source_uid`, `game_version`, `player_level`, `frontier_main_mission` (the tracked main), `floor_id`, `table_count`. |
| `POST /admin/api/snapshots` | `{uid, name, description?}`. Saves the uid's current save. The id is a slug of the name, with `-2`, `-3`… appended on collision. |
| `POST /admin/api/snapshots/{id}/spawn` | Creates a new account from the snapshot. Same body and defaults as clone. |
| `POST /admin/api/accounts/{uid}/snapshot/restore` | `{id, nickname?}`. Kicks `{uid}` if online and replaces its save with the snapshot. |
| `DELETE /admin/api/snapshots/{id}` | Deletes the file. |

- Ids must match `^[A-Za-z0-9_-]{1,64}$`. Anything else, including path traversal, is a 400.
- The payload maps table name → column name → column text, with BLOB columns (MemoryPack and protobuf) as base64. A table this build does not know is skipped with a warning. A missing column reads back like a column added by a schema migration. A file or column that cannot be read is a 400, and the target save is left untouched.
- A snapshot is tied to the MemoryPack layout of the build that wrote it. After a schema change, restore old snapshots on the old build, or check them on a copy first.

## Story checkpoint provisioner

`GameServer/Game/Mission/StoryCheckpointProvisioner.cs` builds a save standing at a chosen main mission, optionally at one of its sub-missions. It does not flip mission ids. It replays the story on a fresh offline player:

1. Wipe the save (kicking an online session) and initialise a fresh player with the mission system on. The Kafka prologue starts with trial Kafka (SpecialAvatar `1001005`) in the lineup.
2. Order the prerequisite main missions with `MissionManager.StoryPrerequisites`: `MultiSequence` takes on Main rows plus `NextTrackMainMission`, then a topological sort. A chain that stops short of 1000101 is bridged with the mission listed just before it in `Config/Custom/WarpStoryGates.json`.
3. Accept each main mission and finish its running sub-missions in id order through `MissionManager`. Finish actions, narrative hooks, rewards, trial lineups, story lines and level tasks all run as in play.
   - Dialogue choices (`CustomValue` subs) are answered through `HandleCustomValue` with the first open alternative, so the choice is also recorded on the main.
   - A finished raid is left, which resets the raid's own mains, as the official server does.
   - A main that still cannot close is finished by force and listed in `stalled_missions`.
4. At the target, `stop_mode` decides where the walk stops:
   - `before`: the target is accepted. With a target sub, only that sub's own prerequisites are finished, until the sub is accepted.
   - `after`: the target is finished.
5. Clean-up and normalisation (see below), then one save.

If the replay throws after the wipe, the previous save is put back.

### Prologue (1000101)

The receptacle room's gender/name page is CreateCharacter `100010134`. Its client request (`HandlerSetPlayerInfoCsReq`) is mirrored right before the walk finishes that sub:

- `SetHeroGender`
- Warrior path
- The formal Trailblazer and March 7th joining the real team

Checkpoints **before** 100010134 is finished:

- `IsGenderSet` stays false and trial Kafka stays in the lineup.
- Silver Wolf (`1001006`) is in the lineup between 100010110 and 100010123.
- The skipped PropState consoles on floor 20001001, group 5, are saved in their target state, together with the doors they unlock:
  - Open: `300002`, `300004`, `300006`, `300018`
  - Closed: `300029`
- The spawn point is the target sub's waypoint. A sub without one (talks, CreateCharacter) uses the nearest anchor to the waypoint of its finished prerequisite. The receptacle preset lands on group 14 anchor 9, next to the KeyFinal console.

Checkpoints **after** 100010134:

- The prologue trials are removed.
- The formal Trailblazer (`trailblazer_gender`; unset means Stelle) is in the lineup.
- `trailblazer_name` is applied.
- Story paths unlocked by finished chapters are granted.

### Clean-up and normalisation

- **Story lines and raids.** A story line whose end sub is finished is left. An unfinished raid that does not belong to the target is abandoned.
- **Prop states.** Every finished PropState sub saves its prop's target state, in finish order. Groups with `SaveType` `Reset` are skipped.
- **Tutorials (`tutorials_mode`).**
  - `finish_prior` (default): finishes every tutorial whose trigger or finish mission is already finished, except tutorials triggered by the target itself. For example, 3002 stays untouched after 1000301.
  - `keep_open`: only the fresh-account seed.
  - `finish_all`: finishes every tutorial.
- **Teleports (`teleports_mode`).** Every checkpoint prop (`MappingInfoID` with a `CheckPointDisable`/`CheckPointEnable` state) on the floors in scope is recorded as reached and saved `CheckPointEnable`. Only Maze, Town and Train planes count.
  - `reached_planes` (default): floors entered during the replay, floors of finished subs, and the spawn floor.
  - `all`: every floor with a map entrance.
  - `none`: no teleports.
- **Tracking and spawn.** The target is tracked (with `after`, its successor). The account spawns on the target's floor through `FindEntranceForFloor`, at the waypoint anchor when one exists.
- **`progression_preset`.**
  - `test_ready` (default): PlayerLevel/WorldLevel gates of the chain are raised instead of bypassed. Formal avatars are levelled to the world level's cap (`20 + 10 × WorldLevel`, at most 80).
  - `live_like`: levels come only from the replayed rewards, and every unmet gate is accepted anyway and listed in `bypassed_gates`.
- **`grant_test_currency`.** Adds 16000 Stellar Jade, 1,000,000 Credits, 50 Star Rail Passes and 50 Special Passes.
- **`include_side_missions`.** Also replays non-Main rows that the chain names in a `MultiSequence` take.

### API

- `GET /admin/api/story/catalog` returns the `WarpStoryGates.json` versions in order, each with its chapters and Main missions. Each mission includes `id`, `chapter_id`/`chapter_name` (from the chapter gates, or the version's only chapter), `version`, `type`, `floor_id` and its `sub_missions` (`id`, `finish_type`, `way_point_floor_id`, `way_point_group_id`). The server has no TextMap, so the console resolves mission names from the GM handbook.
- `presets` lists the one-click checkpoints. The version entries are the version's earliest chained mission (Manual-take rows skipped). On the 4.7 cache they resolve to:

| Preset | Main | Sub | Stop |
| --- | --- | --- | --- |
| `prologue-kafka-start` | 1000101 | 100010102 | before |
| `prologue-receptacle` | 1000101 | 100010134 | before |
| `post-prologue-wakeup` | 1000201 | | before |
| `warp-tutorial` | 1000301 | | after |
| `jarilo-vi` | 1010002 | | before |
| `xianzhou-luofu` | 1020101 | | before |
| `penacony` | 1030101 | | before |
| `amphoreus` | 1040101 | | before |
| `version-4-0` | entry of 4.0 | | before |
| `version-4-7` | 1054704 | | before |

- `POST /admin/api/accounts/provision-story` creates an account and provisions it. It takes the clone body plus `preset?`, `target_main_mission_id`, `target_sub_mission_id?`, `stop_mode` (`before`/`after`), `trailblazer_gender` (`man`/`woman`/`unset`), `trailblazer_name?`, `progression_preset` (`test_ready`/`live_like`), `tutorials_mode`, `teleports_mode`, `include_side_missions`, `grant_test_currency`. Unknown missions, a sub of another main, and unknown option values are a 400, and no account is created.
- `POST /admin/api/accounts/{uid}/provision-story` resets an existing account to the checkpoint.
- Both return `{account, generated_password, report}`. The report holds `missions_finished`, `sub_missions_finished`, `teleports_unlocked`, `tutorials_marked_finished`, `prop_states_restored`, `spawn_plane_id`/`spawn_floor_id`/`spawn_entry_id`, `tracking_main_mission_id`, `gender_set`, `player_level`/`world_level`, `lineup`, `bypassed_gates`, `stalled_missions`, `warnings` and `elapsed_ms`.
- Provisioning is serialised server-wide. On the 4.7 cache the 4.7 entry replays about 275 mains in roughly 5–11 s.

### Operating notes

- "Jump to story point" on an existing account wipes its save. Save a snapshot first to keep it.
- The replay never touches `Config/Resource.bin`, `Proto/` or the database schema.
- Server regressions do not replace a client check. Verify by hand:
  - Spawn from `prologue-receptacle`: Kafka should stand in the receptacle room, and interacting should open gender/name selection.
  - Spawn at Jarilo-VI, Penacony and 4.7: map teleports should be unlocked, the tracked quest shown, no tutorial pop-up storm, and doors and NPCs on the floor working.
  - Play to any spot, save a snapshot, spawn from it, and log in: position and state should match.
