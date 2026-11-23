# 存档编辑器

[English](player-editor.md) | 简体中文

后台的**存档编辑器**页面（`/admin/index.html#/editor/<uid>`，侧边栏分组 *Players*）按分区编辑单个账号的游戏存档：资料与货币、角色、背包、队伍、剧情与世界，以及原始存档表。账号页面在资料页和 *Player data* 页提供入口。测试账号的创建仍在账号页面。

## 修改如何生效

每次修改都通过 `GameServer/Game/Player/OfflinePlayerSession.cs` **离线**执行：

1. 以租约占用该 UID。租约持有期间，`PlayerGetTokenCsReq` 以 `RetSystemBusy` 拒绝登录，第二个工具（另一次编辑、快照恢复、剧情进度生成）得到 409。
2. 停止该 UID 的所有会话（在线玩家会被断开；后台会提示）。
3. 带分区 `version` 的修改会与已保存的存档比对。页面加载后存档发生过变化（通常是玩家在线时造成的）则返回 409，不做任何修改；页面会重新加载该分区。
4. 已保存的存档像登录时一样加载到没有会话的 `PlayerInstance` 中，修改通过游戏自身的管理器执行：`AvatarManager.AddAvatar`、`InventoryManager.AddItem`、`MissionManager`（完成动作和叙事钩子会运行）、`TryPlaceAtEntrance` 等。然后写入存档。
5. 修改抛出异常时放回之前的存档。成功时，修改前的存档保留为该 UID 的**撤销点**。

客户端无法显示的值会以 400 拒绝：超出 1–80（角色、光锥）或均衡等级表的等级、等级不允许的晋阶、0–6 以外的星魂、1–5 以外的叠影、单次发放超过 100 个光锥/遗器、未知 ID、同一队伍中重复的角色、当前队伍为空、移除开拓者。

## 撤销

撤销点每个 UID 一个文件，即数据库旁的 `Snapshots/undo/<uid>.json`（快照格式；不出现在快照列表中）。每次编辑器修改、快照恢复、克隆到已有 UID 以及对已有存档生成剧情进度都会替换它。*Undo last change*（`POST .../editor/undo`）放回该存档，并把被替换的存档作为新的撤销点，因此再撤销一次就是重做。删除账号会删除其撤销文件。

## 分区与 API

所有路由都在 `/admin/api/players/{uid}/editor` 下，需要管理员会话。

| 分区 | 读取 | 修改 |
| --- | --- | --- |
| 概要 | `GET` → `exists`、`online`、`busy`（有工具占用该 UID）、`mission_enabled`、`undo`（`label`、`created_at`）、等级上限、`delete_on_logout` | `POST /undo` |
| 资料 | `GET /profile` → 昵称、签名、头像、等级、经验、均衡等级、信用点、星琼、古老梦华、开拓力及后备开拓力、月卡到期时间、性别（`AvatarManager.IsHeroGenderPending` 时 `gender_set` 为 false）、`version` | `PUT /profile`（所有字段 + `version`；`gender`：`keep`/`man`/`woman`）。提升等级会执行升级时的任务接取。性别通过游戏的 `SetHeroGender` 切换：保留命途，并配对主角的命途 ID；队伍保存的是基础 ID 8001，因此无需修改。剧情中的性别选择尚未到来时拒绝修改。 |
| 角色 | `GET /avatars` → 每个已拥有角色及其命途、等级、晋阶、星魂、装备的光锥和遗器（`relics`：部位、唯一 ID、物品 ID）、`traces_maxed`，以及 `version` | `POST /avatars {avatar_id}`；`PUT /avatars/{id} {level, promotion?, rank, max_traces, version}`（星魂作用于所有命途；不提供 `promotion` 时取等级允许的最低值）；`PUT /avatars/{id}/gear {light_cone_unique_id?, relics: {"1".."6": unique_id}}`（当前命途；0 表示卸下，省略表示保留；遗器必须匹配部位；他人穿戴的装备按游戏内方式交换）；`DELETE /avatars/{id}`（离开所有队伍，卸下装备） |
| 背包 | `GET /inventory` → 材料、光锥、遗器 | `POST /inventory/grant {item_id, count, level?, rank?, promotion?}`（遗器属性按掉落方式随机）；`PUT /inventory/items/{item_id} {count}`（精确数量，0 表示移除）；`PUT /inventory/light-cones/{unique_id} {level, promotion?, rank}`；`POST /inventory/remove {unique_ids}`（从穿戴者身上卸下） |
| 队伍 | `GET /lineups` → 队伍 1–9 及其槽位、当前队伍、生效中的剧情/raid 队伍、`version` | `PUT /lineups/{index} {slots: [{base_avatar_id, special_avatar_id}], make_current, version}`。已在队伍中的剧情试用角色可以保留或移除，但不能新增。 |
| 剧情 | `GET /story` → 任务系统、追踪中的主线、运行中的主线及其运行中的子任务、已完成的主线、位置、传送点和教程数量 | `POST /story/missions {action, mission_id?, count?, strict?}`，`action` 为 `advance`、`rollback`、`repair`、`finish_sub`、`finish_main`、`accept_main`、`reaccept_main`、`remove_main`、`track`、`enable`、`disable`（即 `/mission` 命令的操作）；`POST /story/teleports {mode: reached_planes|all}`；`POST /story/tutorials`；`PUT /position {entry_id}` |
| 原始数据 | `GET /raw` → 游戏数据表及存档包含哪些表；`GET /raw/{table}` → `json`、`editable`、`version` | `PUT /raw/{table} {json, version}` 只替换该表，存档其余部分保留。 |

`GET /admin/api/editor/catalog` 为选择器提供数据：可玩角色（`id`、`base_id`、`rarity`）、光锥、遗器和其他物品 ID，以及地图入口。名称在浏览器中从 GM 手册读取；没有 `Config/Handbook` 时选择器只显示 ID。

### 剧情点

剧情分区打开剧情对话框，有两种模式：

- **推进**（在 `POST /admin/api/accounts/{uid}/provision-story` 上使用 `keep_save: true`）：生成器加载已保存的存档而不是清空它，只游玩通往目标途中仍未完成的任务。角色、物品和已完成的任务保留。已经越过该点的存档为 400，并保持原样。
- **跳转**：现有的清空并重放流程（[剧情进度测试账号](story-checkpoints.zh-CN.md)）。

两者都把之前的存档保留为撤销点。

### 原始数据表

JSON 是该表的行经 Newtonsoft 序列化后的结果。只有当 JSON 能精确读回为已保存的行（经数据库往返后逐列比较）时该表才可编辑；其他表只读显示。在 4.7 缓存上，已生成进度的存档的全部 35 张表都能往返。错误的值仍可能导致无法登录；请使用撤销。

## 登出即删除的测试账号

标记了 `DeleteOnLogout` 的账号在其玩家会话结束时（登出、断开连接或被踢下线）连同存档一起被删除（留下墓碑记录，与管理员删除相同）。被同一 UID 的新登录替换的会话，以及被编辑器/进度生成/恢复租约停止的会话，不计入。

- 创建测试账号时设置（克隆、快照生成和剧情进度生成时使用 `delete_on_logout: true`；对话框中的 *Delete account after logoff* 开关）。
- 在已有账号的 *Danger* 页切换（`PUT /admin/api/accounts/{uid}/delete-on-logout {enabled}`）。
- 该标记是 schema 迁移新增的 `Account` 列；已有账号读取为关闭。

## 服务器回归未覆盖的内容

- 修改后的客户端表现（角色界面、背包、队伍界面、修改位置后的地图）需要在游戏内检查。
- 通过真实客户端断开连接删除的登出即删除账号：会话结束逻辑是直接检查的（`EditorChecks`），而不是通过真实的 KCP 会话。

页面的每个写操作都已在浏览器中针对 `--serve --with-resources` 测试夹具执行过一次（含性别的资料、角色添加/编辑/装备/移除、发放、材料数量、光锥编辑、卸下装备、队伍、任务按钮、传送点、教程、位置、原始存档、撤销、Danger 页开关）；这是页面检查，不是客户端检查。
