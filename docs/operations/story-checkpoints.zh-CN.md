# 剧情进度测试账号、账号克隆与存档快照

[English](story-checkpoints.md) | 简体中文

后台（账号页面）可以创建停在开拓任务剧情任意位置的测试账号，也可以把一个账号的完整存档复制到新账号，或把存档保存为命名快照。这些操作都**只替换游戏数据**：账号行（用户名、密码、角色、令牌、邮箱、封禁）从不复制或覆盖。

## 账号克隆

`POST /admin/api/accounts/{uid}/clone` 创建一个新账号，并给它 `{uid}` 存档的深拷贝。

- 请求体：`{username?, password?, email?, uid?, role?, nickname?, delete_on_logout?}`。用户名为空时为 `<source>_clone_<hex>`。密码为空时自动生成，并只以 `generated_password` 返回一次。邮箱为空时为 `<username>@local.test`。`nickname` 重命名复制出的开拓者。`delete_on_logout` 使其成为一次性账号（见 [存档编辑器](player-editor.zh-CN.md#登出即删除的测试账号)）。
- 在线的源账号会先落盘。复制与数据库保存再读取走同样的行往返（`EntityStore.DeepCopy`），因此克隆与源账号不共享任何对象。所有行在一个 SQLite/MySQL 事务中写入。
- `Friend` 和 `assist_reward_data` 不复制，因为它们记录的是其他玩家的 UID。克隆账号没有好友，也没有待领取的支援奖励。
- 复制失败时，新账号会再次被注销（删除并留下墓碑记录）。

## 存档快照

快照是数据库旁 `Snapshots/` 下的文件（默认 `Config/Database/Snapshots/<id>.json`，已被 git 忽略）。它们是玩家存档数据：请像数据库一样备份和保护。

| 路由 | 用途 |
| --- | --- |
| `GET /admin/api/snapshots` | 列出快照元数据：`id`、`name`、`description`、`created_at`、`source_uid`、`game_version`、`player_level`、`frontier_main_mission`（追踪中的主线）、`floor_id`、`table_count`。 |
| `POST /admin/api/snapshots` | `{uid, name, description?}`。保存该 UID 的当前存档。ID 由名称生成，冲突时追加 `-2`、`-3`…。 |
| `POST /admin/api/snapshots/{id}/spawn` | 从快照创建新账号。请求体和默认值与克隆相同。 |
| `POST /admin/api/accounts/{uid}/snapshot/restore` | `{id, nickname?}`。若 `{uid}` 在线则踢下线，并用快照替换其存档。 |
| `DELETE /admin/api/snapshots/{id}` | 删除文件。 |

- ID 必须匹配 `^[A-Za-z0-9_-]{1,64}$`。其他值（包括路径穿越）均为 400。
- 内容是 表名 → 列名 → 列文本 的映射，BLOB 列（MemoryPack 和 protobuf）为 base64。当前构建不认识的表会被跳过并记录警告。缺失的列按 schema 迁移新增列的方式读取。无法读取的文件或列为 400，目标存档保持不变。
- 快照绑定到写入它的构建的 MemoryPack 布局。schema 变更后，请在旧构建上恢复旧快照，或先在副本上检查。

## 剧情进度生成器

`GameServer/Game/Mission/StoryCheckpointProvisioner.cs` 生成停在指定主线任务（可选指定其某个子任务）的存档。它不直接改写任务 ID，而是在一个全新的离线玩家上重放剧情：

1. 清空存档（踢下在线会话），以开启任务系统的方式初始化新玩家。卡芙卡序章开始时队伍中为试用卡芙卡（SpecialAvatar `1001005`）。
2. 用 `MissionManager.StoryPrerequisites` 对前置主线任务排序：对 Main 条目的 `MultiSequence` take 加上 `NextTrackMainMission` 做拓扑排序。在 1000101 之前中断的链条，用 `Config/Custom/WarpStoryGates.json` 中排在它之前的任务衔接。
3. 通过 `MissionManager` 接取每个主线任务，并按 ID 顺序完成其运行中的子任务。完成动作、叙事钩子、奖励、试用阵容、故事线和关卡任务都与实际游玩时一样运行。
   - 对话选项（`CustomValue` 子任务）通过 `HandleCustomValue` 以第一个可选项作答，因此选择也会记录在主线上。
   - 完成的 raid 会被退出，这会重置该 raid 自己的主线，与官方服务器一致。
   - 仍无法关闭的主线会被强制完成，并列入 `stalled_missions`。
   - 链条途中接取但并不经过的 Main 条目按剧情顺序游玩。在每个链条主线之前，先游玩所有链深度更小的运行中 Main 条目；在目标之前，游玩所有排在它之前的条目。因此它们的完成动作发生在玩家遇到它们的位置：1000204 的 `ChangeLineup` 在后续章节调整队伍之前加入丹恒。例如：1000204（丹恒在黑塔空间站加入）从 1000201 分支出来，之后没有任务再接续它。如果让它一直运行，只在 `FinishMainMission 1000204` 时关闭的教程 3003 会在登录时弹出并阻塞玩家输入。
4. 到达目标时，由 `stop_mode` 决定停在哪里：
   - `before`：目标已接取。指定了目标子任务时，只完成该子任务自身的前置，直到该子任务被接取。
   - `after`：目标已完成。
5. 清理与规范化（见下文），然后保存一次。

如果清空之后重放抛出异常，会放回之前的存档。成功时，已有账号之前的存档成为其 [存档编辑器撤销点](player-editor.zh-CN.md#撤销)。

使用 `keep_save: true`（仅限已有账号）时，第 1 步加载已保存的存档而不是清空，因此只游玩途中仍未完成的任务，角色、物品和已完成的任务保留。已经越过目标的存档为 400，并保持不变。

### 序章（1000101）

培养舱房间的性别/名称页面是 CreateCharacter `100010134`。在重放完成该子任务之前，会镜像其客户端请求（`HandlerSetPlayerInfoCsReq`）：

- `SetHeroGender`
- 毁灭命途（Warrior）
- 正式开拓者和三月七加入正式队伍

**在** 100010134 完成**之前**的进度点：

- `IsGenderSet` 保持 false，试用卡芙卡留在队伍中。
- 在 100010110 与 100010123 之间，银狼（`1001006`）在队伍中。
- 楼层 20001001 分组 5 上被跳过的 PropState 控制台会以目标状态保存，连同它们解锁的门：
  - 打开：`300002`、`300004`、`300006`、`300018`
  - 关闭：`300029`
- 出生点为目标子任务的路点。没有路点的子任务（对话、CreateCharacter）使用其已完成前置的路点最近的锚点。培养舱预设落在分组 14 锚点 9，紧邻 KeyFinal 控制台。

100010134 **之后**的进度点：

- 移除序章试用角色。
- 正式开拓者（`trailblazer_gender`；未设置即星）在队伍中。
- 应用 `trailblazer_name`。
- 发放已完成章节解锁的剧情命途。

### 清理与规范化

- **故事线与 raid。** 结束子任务已完成的故事线会被退出。不属于目标的未完成 raid 会被放弃。
- **物件状态。** 每个已完成的 PropState 子任务按完成顺序保存其物件的目标状态。`SaveType` 为 `Reset` 的分组被跳过。
- **教程（`tutorials_mode`）。**
  - `finish_prior`（默认）：完成所有触发任务或完成任务已经完成的教程，目标自身触发的教程除外。例如 1000301 之后 3002 保持不动。它也会完成存档已满足其全部触发条件（任务、`PlayerLevel`、`AnyAvatarToLevel`、`GetAvatar` 和 `TutorialFinish`）的自由游玩教程（`OnMazeMainPage`）。这类教程会在第一帧弹出并阻塞输入，例如在罗浮的 4003（PlayerLevel 15，没有完成条件）。由运行中的目标子任务打开的教程保持不动。此步骤在 `progression_preset` 之后运行，因此等级已是最终值。
  - `keep_open`：只保留新账号的初始状态。
  - `finish_all`：完成所有教程。
- **传送点（`teleports_mode`）。** 范围内楼层上的每个检查点物件（带 `CheckPointDisable`/`CheckPointEnable` 状态的 `MappingInfoID`）被记录为已到达，并保存为 `CheckPointEnable`。只计入 Maze、Town 和 Train 平面。
  - `reached_planes`（默认）：重放期间进入过的楼层、已完成子任务的楼层，以及出生楼层。
  - `all`：所有有地图入口的楼层。
  - `none`：不解锁传送点。
- **追踪与出生点。** 追踪目标任务（`after` 时为其后继）。账号通过 `FindEntranceForFloor` 出生在目标所在楼层，有路点锚点时出生在该锚点。
- **`progression_preset`。**
  - `test_ready`（默认）：链条上的 PlayerLevel/WorldLevel 门槛通过提升等级满足，而不是绕过。正式角色升到均衡等级上限（`20 + 10 × WorldLevel`，最多 80）。
  - `live_like`：等级只来自重放的奖励，所有未满足的门槛仍会被接受，并列入 `bypassed_gates`。
- **`grant_test_currency`。** 增加 16000 星琼、1,000,000 信用点、50 张星轨通票和 50 张星轨专票。
- **`include_side_missions`。** 同时重放链条在 `MultiSequence` take 中提到的非 Main 条目。

### API

- `GET /admin/api/story/catalog` 按顺序返回 `WarpStoryGates.json` 中的版本，每个版本包含其章节和 Main 任务。每个任务包含 `id`、`chapter_id`/`chapter_name`（来自章节门槛，或该版本唯一的章节）、`version`、`type`、`floor_id` 及其 `sub_missions`（`id`、`finish_type`、`way_point_floor_id`、`way_point_group_id`）。服务器没有 TextMap，因此后台从 GM 手册解析任务名称。
- `presets` 列出一键进度点。版本条目取该版本最早的链条任务（跳过 Manual take 条目）。在 4.7 缓存上解析为：

| 预设 | 主线 | 子任务 | 停止 |
| --- | --- | --- | --- |
| `prologue-kafka-start` | 1000101 | 100010102 | before |
| `prologue-receptacle` | 1000101 | 100010134 | before |
| `post-prologue-wakeup` | 1000201 | | before |
| `warp-tutorial` | 1000301 | | after |
| `jarilo-vi` | 1010002 | | before |
| `xianzhou-luofu` | 1020101 | | before |
| `penacony` | 1030101 | | before |
| `amphoreus` | 1040101 | | before |
| `version-4-0` | 4.0 的入口 | | before |
| `version-4-7` | 1054704 | | before |

- `POST /admin/api/accounts/provision-story` 创建账号并生成进度。它接受克隆的请求体，外加 `preset?`、`target_main_mission_id`、`target_sub_mission_id?`、`stop_mode`（`before`/`after`）、`trailblazer_gender`（`man`/`woman`/`unset`）、`trailblazer_name?`、`progression_preset`（`test_ready`/`live_like`）、`tutorials_mode`、`teleports_mode`、`include_side_missions`、`grant_test_currency`。未知任务、属于其他主线的子任务以及未知选项值均为 400，且不会创建账号。
- `POST /admin/api/accounts/{uid}/provision-story` 把已有账号重置到该进度点，或在 `keep_save: true` 时推进到该进度点。
- 两者都返回 `{account, generated_password, report}`。报告包含 `missions_finished`、`sub_missions_finished`、`teleports_unlocked`、`tutorials_marked_finished`、`prop_states_restored`、`spawn_plane_id`/`spawn_floor_id`/`spawn_entry_id`、`tracking_main_mission_id`、`gender_set`、`player_level`/`world_level`、`lineup`、`bypassed_gates`、`stalled_missions`、`warnings` 和 `elapsed_ms`。
- 进度生成在全服范围内串行执行。在 4.7 缓存上，4.7 入口会完成约 300 个主线，耗时约 5–11 秒。

### 操作说明

- 对已有账号执行「跳转到剧情点」会清空其存档；之前的存档是 [存档编辑器](player-editor.zh-CN.md) 中的撤销点。「推进」会保留存档。
- 重放从不修改 `Config/Resource.bin`、`Proto/` 或数据库 schema。
- 服务器回归不能替代客户端检查。请手动验证：
  - 从 `prologue-receptacle` 生成：卡芙卡应站在培养舱房间，交互应打开性别/名称选择。
  - 在雅利洛-VI、匹诺康尼和 4.7 生成：地图传送点应已解锁，显示追踪任务，没有成串弹出的教程，楼层上的门和 NPC 正常。
  - 游玩到任意位置，保存快照，从快照生成并登录：位置和状态应一致。
