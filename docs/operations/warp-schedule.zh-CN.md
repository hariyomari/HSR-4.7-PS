# 跃迁排期

[English](warp-schedule.md) | 简体中文

服务器可以从往期卡池中提供限定跃迁卡池，而不只是 `Config/Custom/Banners.json` 中的固定列表。设置位于 `Config.json` 的 `ServerOption.Warps`，可在后台（跃迁页面，或设置页的 `warps` 分组）修改，无需重启。

## 模式

| 模式 | 提供的限定卡池 | 说明 |
| --- | --- | --- |
| `Static`（默认） | `Banners.json` 中的限定卡池及其中写明的时间窗口 | 跃迁排期出现之前的行为。 |
| `Rotation` | 每个轮换周期提供历史池中的一个阶段（`PhaseLineup` 为 `Version` 时为整个版本），按历史顺序，最新阶段之后从头循环 | 卡池以当前周期作为时间窗口。 |
| `Manual` | 管理员在 `ServerOption.Warps.Manual` 中挑选的列表 | 每个条目可以有自己的时间窗口和 UP 物品。 |

常驻池（`Newbie`、`Normal`）在所有模式下都来自 `Banners.json`。`Rotation` 和 `Manual` 模式不提供 `Banners.json` 中的限定卡池。同时提供的限定卡池数量没有上限。

在列表为空时切换到 `Manual`，会用当时正在开放的卡池填充列表，因此切换本身不会改变玩家看到的内容。切换到 `Rotation` 而没有设置开始日期时，轮换从今天开始。

## 常驻池

`Banners.json` 自带两个正式服常驻池。二者在卡池列表中都没有时间窗口。

- **群星跃迁（1001，`Normal`）。** 消耗星轨通票（101），单抽或十连。跃迁教程（3002）在开拓任务「A Moment of Peace」（主线 1000301）之后开始，该任务发放那一张通票。打开教程会启用一次性规则：该账号下一次群星跃迁必定获得艾丝妲（1009），且必须是单抽；十连会以 `RET_GACHA_FIRST_GACHA_MUST_ONE`（3003）拒绝，不扣除任何消耗。获得艾丝妲的这一抽消耗通票，出现在跃迁记录中，并在常驻计数中按 4★ 计算。规则按账号保存，不会启用第二次。从未打开该教程的账号不受影响。
- **始发跃迁（4001，`Newbie`）。** 消耗星轨通票（101），仅限十连，每次 8 张。最迟在第 50 抽获得七名常驻 5★ 角色之一，且总共只允许 50 抽；之后不再发送该卡池。每 10 抽至少出现一个 4★ 或以上。其保底计数与群星跃迁分开。其 `rateUpItems5`（同样的七名角色）只用于卡池展示。账号的抽数通过 `newbie_pull_count` 发给客户端：`GachaInfo` 字段 15（通过游戏内测试确认：客户端显示的「剩余跃迁次数」为其自身的 50 减去该值）和 `DoGachaScRsp` 字段 12（混淆名相同，因此推断为同一个值；尚未在抽卡后观察到）。50 抽上限由服务器强制执行。

## 联动卡池

历史池中的联动卡池（四个联动角色卡池及其四个光锥卡池，ID 5001–5004 和 6001–6004）与常驻池一样自成一组。开启 `CollabBanners`（默认）时，所有联动卡池在每种模式下都向所有玩家提供，以当前轮换周期为时间窗口：它们不属于任何阶段阵容，剧情过滤不会隐藏它们，手动列表也不能包含它们。关闭时一个都不提供。`Banners.json` 中的联动条目永远不会提供；这一组来自历史池。

## 剧情过滤

开启 `Story` 后，每个玩家只会看到其剧情已到达的版本的限定卡池。它叠加在 `Static` 和 `Rotation` 之上；`Manual` 忽略它，因为列表是管理员有意挑选的。

| 模式 | 开启 `Story` 时 |
| --- | --- |
| `Static` | 同时提供所有已解锁的历史卡池，以当前轮换周期为时间窗口。不提供 `Banners.json` 中的限定卡池。剧情后期这会是一个很长的列表：尚未测试客户端如何处理这么多卡池。 |
| `Rotation` | 只轮换已解锁的阶段，按历史顺序。`RotationStartPhase` 所指阶段已解锁时从该阶段开始，否则从最早的阶段开始。 |
| `Manual` | 无变化。 |

- **版本何时解锁。** `Config/Custom/WarpStoryGates.json` 为每个新增了开拓任务剧情的版本列出其章节的主线任务。其中任一任务被接取或完成，玩家即视为到达该版本，因此版本在其章节开始时解锁。1.0 版本始终解锁。
- **没有新剧情的版本**（1.1、1.4–1.6、2.4–2.6）与其前一个剧情版本一起解锁：处于 1.0 剧情的玩家会看到 1.0 和 1.1 的卡池。
- **顺序按版本，而非任务 ID。** 3.8 的章节（「Memories are the Prelude to Dreams」）的任务 ID 小于 3.6 和 3.7。
- **没有可读取的剧情。** 任务系统关闭的玩家（服务器 `EnableMission` 为 false，或按账号覆盖）在所有模式下都按原样获得 `Banners.json` 的限定卡池。门槛文件缺失或为空时同样如此。
- **按章节（`StoryByChapter`，默认关闭）。** 关闭时按版本设门槛，如上所述。开启时（且 `Story` 生效），1.0 和 1.1 的四个阶段改为按章节解锁；从 1.2 起每个版本恰好有一个剧情章节，因此其他版本不变。新账号在到达雅利洛-VI 之前看不到任何限定卡池。

  | 阶段 | 5★ | 随章节解锁（`MainMission.json` 中的 `ChapterID`） |
  | --- | --- | --- |
  | `1.0-1` | 希儿 | 101001「In the Withering Wintry Night」（雅利洛-VI） |
  | `1.1-1` | 银狼 | 101001 |
  | `1.0-2` | 景元 | 102001「Windswept Wanderlust」（仙舟「罗浮」第一章） |
  | `1.1-2` | 罗刹 | 102001 |

  章节在其任一主线任务被接取或完成、到达更晚的章节门槛、或玩家到达比该章节更新的版本时视为已到达。章节未到达的阶段即使其版本已解锁也保持隐藏。
- 列表在每次跃迁请求时按玩家计算，`DoGacha` 检查同一列表，因此无法抽取已锁定的卡池。后台跃迁页面显示全服排期，并会注明过滤器按玩家缩小了列表。

### 生成的数据

`scripts/generate-warp-data.py <resource dir>` 从包含 `ExcelOutput/` 和 `TextMap/` 的原始游戏数据目录（例如 `TurnBasedGameData`）向 `Config/Custom` 写入两个文件：

- `WarpStoryGates.json`：每个剧情章节的主线任务（`MainMission.json`，`Type` 为 `Main`，按 `ChapterID` 分组），再按版本分组。游戏数据没有记录章节随哪个版本上线，因此脚本自带这张表，取自 Fandom wiki 的 [Trailblaze Mission change history](https://honkai-star-rail.fandom.com/wiki/Trailblaze_Mission/Change_History)。新版本需要在表中新增一行；数据中出现表未覆盖的剧情章节时脚本会停止。服务器自身没有章节数据，所以任务 ID 需要显式列出。`ChapterGates` 列表保存 `StoryByChapter` 使用的按章节门槛（阶段、版本、章节 ID 与名称，以及该章节的主线任务）。
- `WarpItemNames.json`：角色和光锥的英文与简体中文名称，供后台跃迁页面使用（卡片、编辑对话框、按名称搜索）。ID 不在文件中时页面回退到 GM 手册。

## 配置（`ServerOption.Warps`）

| 键 | 默认值 | 含义 |
| --- | --- | --- |
| `Mode` | `"Static"` | `Static`、`Rotation` 或 `Manual`（不区分大小写）。未知值按 `Static` 处理。 |
| `RotationDays` | `7` | 一个轮换周期的天数（后台设置接受 1..365）。没有结束时间的手动条目也按此周期滚动。 |
| `RotationStart` | `""` | `yyyy-MM-dd`；周期 0 从该日服务器本地时间零点开始。为空 = 1970-01-01 UTC。 |
| `RotationStartPhase` | `""` | 周期 0 提供的阶段键（例如 `"4.7-1"`）。为空或未知 = 最早的阶段。 |
| `PhaseLineup` | `"Started"` | `Rotation` 和载入阶段中一个阶段由哪些卡池组成：`Started` = 只有在该阶段开放的卡池；`Overlap` = 还包括该阶段开始时仍在开放的所有更早卡池（正式服阵容）；`Version` = 该版本两个阶段中开放的所有卡池，轮换每个周期前进一个版本。未知值按 `Started` 处理。 |
| `CollabBanners` | `true` | 在每种模式下把联动卡池作为独立一组提供（见上文）；`false` = 从不提供。 |
| `Story` | `false` | 剧情过滤，见上文。 |
| `StoryByChapter` | `false` | 剧情过滤生效时，1.0 和 1.1 的阶段按章节而非按版本解锁（见上文）。`Story` 关闭或处于 `Manual` 时无效。 |
| `Manual` | `[]` | 手动模式条目，见下文。 |

手动条目字段：

| 字段 | 含义 |
| --- | --- |
| `GachaId` | 符合条件的历史池中某个卡池的 ID。每个 ID 只能出现一次。 |
| `BeginTime`、`EndTime` | Unix 秒。`0` = 该边界取自当前轮换周期，因此卡池永不过期。两者都设置时，开始必须早于结束。 |
| `RateUpItems5`、`RateUpItems4` | `null` = 使用历史条目的 UP 物品。否则每个 ID 都必须存在于游戏数据中，且 `RateUpItems5` 不能为空。UP 展示面板按覆盖值重建。 |
| `PairedGachaId` | 后台面板中显示在该角色卡池旁的光锥卡池；`0` = 无。仅为界面提示，不影响实际提供的内容。 |

后台接口会校验整个列表并一次性替换；无效列表会被拒绝，运行中的列表保持不变。手工编辑 `Config.json` 时，请自行做同样的检查，或使用后台 API。

## 历史池

`Config/Custom/Banners.history.json` 保存往期限定卡池（随附文件有 300 个，版本 1.0 到 4.7）。每一行是一个 `Banners.json` 卡池，外加 `version` 和 `phase`，即卡池开始时所在的版本阶段。

- **资格。** 只有满足以下条件的行才会被使用：属于限定类型（`AvatarUp`、`WeaponUp`、`CollaborationAvatarUp`、`CollaborationWeaponUp`），至少有一个 UP 5★，并且 `rateUpItems5`、`rateUpItems4` 和 `prizeItems` 中的每个 ID 都存在于已加载的物品表（`ItemConfigData`）中。如果卡池包含客户端数据中没有的内容，会改为发放兜底物品，因此这些行被排除，并由后台 API 列为 `ineligible`。
- **阶段。** 行按 `version` + `phase` 分组。阶段从其各行（无论是否符合资格）最早的 `beginTime` 开始。`PhaseLineup` 为 `Started` 时，阵容是标记为该阶段的符合资格的卡池。`Overlap` 还包括历史时间窗口覆盖阶段开始时间的所有符合资格的卡池，因此横跨一个版本两个阶段的卡池会出现在其重叠的每个后续阶段中，与正式服一致。`Version` 的阵容是该版本每个阶段的 `Started` 卡池；此时轮换、重启列表和载入阶段都以版本为单位（取其第一个阶段），`RotationStartPhase` 设为 `4.7-2` 之类时从 4.7 版本开始。联动卡池从不属于阶段阵容。阶段按开始时间排序；使用随附文件和完整物品数据时共有 65 个。
- **新的时间窗口。** 历史时间窗口从不按原样提供。轮换会给当前阶段的每个卡池当前周期（`[periodBegin, periodEnd]`，闭区间）。手动条目使用其显式时间或当前周期。
- **模拟服务器时间。** 时间窗口按真实时钟计算，然后按玩家服务器时钟与真实时钟之差平移，因此处于模拟日期（全局 `ServerTime` 或按账号设置）的账号仍会看到卡池处于开放状态。
- **保底**按卡池族（角色活动、光锥活动、两个联动族、常驻、新手）而非按卡池保存。它在同类型的所有卡池之间延续，阵容变化时也一样。

## 后台 API

所有路由都需要管理员会话（`Authorization: Bearer <token>` 或 `X-Admin-Session`），并返回 `{ "retcode": 0, "message": "OK", "data": ... }`；出错时返回 HTTP 400，原因在 `message` 中（没有会话时为 401）。

| 方法与路径 | 请求体 | 结果 |
| --- | --- | --- |
| `GET /admin/api/warps` | — | 模式、`now`、轮换状态（`days`、`start`、`start_phase`、`period_begin`、`period_end`、`current_phase`、`next_phase`）、`permanent`（`Banners.json` 的 Newbie/Normal）、`scheduled`（当前模式下带实际时间窗口的限定卡池，包括尚未开放或已经结束的手动条目；全服视角，不含剧情过滤和联动组）、原始 `manual` 列表、`pool_size`、`phase_count`、`ineligible` ID、过滤设置 `story`、`story_by_chapter`（按配置）、`story_active`（`story` 开启且不是 `Manual`）、`phase_lineup`，以及 `collab_banners` 和 `collab` 组（开启时为实际时间窗口；关闭时为未开放的历史行）。 |
| `GET /admin/api/warps/pool` | — | `phases`（轮换步骤：每个阶段，或 `PhaseLineup` 为 `Version` 时每个版本一个、以其第一个阶段为键；按当前 `PhaseLineup` 设置给出 `key`、`version`、`phase`、`start_time`、`banner_ids`）、带历史时间窗口的符合资格的 `banners`，以及 `names`（`WarpItemNames.json`：`{ "en": { "1306": "Sparkle", ... }, "zh-CN": { ... } }`）。 |
| `PUT /admin/api/warps/manual` | `{ "entries": [{ "gacha_id", "begin_time"?, "end_time"?, "rate_up5"?, "rate_up4"?, "paired_gacha_id"? }] }` | 校验、替换手动列表、切换到 `Manual`、保存 `Config.json`。允许空列表（不提供限定卡池）。 |
| `POST /admin/api/warps/mode` | `{ "mode": "Static" \| "Rotation" \| "Manual" }` | 切换模式（按上文所述填充）并保存。 |
| `POST /admin/api/warps/rotation` | `{ "phase": "4.7-1" }` | 从该阶段于今天重新开始轮换，切换到 `Rotation`，保存。 |

卡池对象包含 `id`、`gacha_type`、`version`、`phase`、`rate_up5`、`rate_up4`、`begin_time`、`end_time`、`running`（时间窗口包含 `now`）和 `default_pair_id`（与角色卡池同期开放的光锥卡池 `id + 1000`，否则为 0）。

设置页提供 `Warps.Mode`、`Warps.RotationDays`、`Warps.RotationStart`、`Warps.RotationStartPhase`、`Warps.Story`、`Warps.StoryByChapter`、`Warps.PhaseLineup` 和 `Warps.CollabBanners`；它会拒绝未知模式或阵容、格式错误的日期，以及不在池中的阶段键。跃迁页面还提供剧情、按章节和联动卡池开关。包含联动卡池的手动列表会被拒绝（HTTP 400）。

## 重新加载与客户端行为

- `/reload banner` 重新读取 `Banners.json`、`Banners.history.json`、`WarpStoryGates.json` 和 `WarpItemNames.json`；卡池在下次使用时重建。替换物品表的资源重新加载也会重新评估资格。
- 排期变更在下一次跃迁请求时生效。玩家下次打开跃迁界面或重新登录时看到变化（未在真实客户端上验证）。
- 未测试：客户端界面如何处理同时存在的大量限定卡池。
