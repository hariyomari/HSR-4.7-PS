# Web 后台回归

测试项目：[`scripts/WebServerRegression/`](../../scripts/WebServerRegression/)

运行后台回归检查：

```powershell
dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj
```

检查无目标命令、目标隔离、异步输出和异常、并发请求、MUIP GET/POST、登录保护及维护接口内存数据；同时验证 `query_gateway` 下发差分宇宙 BuildRef 地址与请求头，以及两个精确 BuildRef JSON 路由；`/language` 对后台控制台返回仅限玩家，玩家自助控制台可固定自身语言并以新语言回复。测试会打印维护接口耗时；异常测试会打印一条预期的错误日志，最终以 `Regression failures: 0` 为通过。

测试使用独立内存数据和临时目录，不加载项目的 `Config.json` 或玩家数据库。模拟连接和命令仅在测试进程内注册。

需要验证浏览器页面时：

```powershell
dotnet run --project scripts/WebServerRegression/WebServerRegression.csproj -- --serve
```

打开 `http://127.0.0.1:21000/admin/index.html`，使用测试密钥 `local-web-regression-only` 登录，完成后按 Ctrl+C 停止。此模式只启动回环地址上的测试后台；配置及存库操作不用于验证。跃迁页面载入仓库中的 `Banners.history.json` 与 `WarpItemNames.json`，并为卡池引用的物品补充占位行，因此可以查看带名称的卡池、按名称搜索和切换剧情筛选。

账号页的「按剧情进度创建」「跳转剧情进度」需要游戏数据，可追加 `--with-resources`（`-- --serve --with-resources`）：只读加载 `Config/Resource.bin`，剧情回放写入本次的临时数据库，快照写入临时数据库目录下的 `Snapshots/`。不加该参数时剧情目录为空、创建会返回 400。

## 凭据撤销与 MUIP 生命周期

安全回归还检查：

- `/sr-tools` 在 RSA 登录解密开启或关闭时，都拒绝空密码／错误密码；正确密码仍进入正常在线状态检查。
- 玩家控制台令牌绑定签发时的密码版本；管理改密及邮件找回密码之后，旧令牌不能继续使用，新登录仍可用。此机制不依赖数据库新增字段。
- MUIP 会话绑定创建时的管理密钥版本；配置变更后，认证、命令、服务器信息和玩家信息接口均拒绝旧会话。后台轮换密钥也会主动清除 MUIP 管理会话。
- 匿名 MUIP 会话达到容量上限时拒绝新增；已过期且被遗弃的会话会在新建时清理，服务也每分钟主动清理一次。总上限 1024，每个 HTTP 来源最多 32 个尚未过期会话；内部短期命令桥接不使用匿名创建接口。

这些用例仅使用临时账号、临时数据库和回环 HTTP，不接触真实玩家存档。根 `/sr-tools` 导入不再允许仅凭 UID 操作，即使关闭 RSA 解密；没有密码的旧账号需要先设置密码。

## 跃迁排期

`WarpChecks.cs` 在管理员登录后运行，覆盖 `/admin/api/warps*` 与设置页 `warps` 分组（行为说明见 [跃迁排期](../operations/warp-schedule.md)）。用例在进程内写入合成数据：`ItemConfigData` 中的少量物品、含常驻池 1001 与限定池 2139 的 `Banners.json`，以及跨三个阶段的历史池（其中一个卡池跨两个阶段，另一个卡池的 5★ 不在物品表中）。检查：

- 未登录访问跃迁接口返回 401；`Static` 模式的 `scheduled` 即 `Banners.json` 的限定池，玩家可见列表同时包含常驻池与限定池。
- 卡池库按阶段分组：默认 `PhaseLineup` 为 `Started`，跨阶段卡池只出现在它开放的阶段；改为 `Overlap` 后出现在两个阶段的 `banner_ids` 中；改为 `Version` 后每个版本只列一项（以首个阶段为键），`banner_ids` 包含该版本两个阶段开放的全部卡池。缺少物品的卡池列入 `ineligible` 且不出现在卡池库内；行数据携带历史时间、版本阶段和默认光锥池配对。
- 切换 `Rotation` 后从今天开始、由最早阶段起轮换，`scheduled` 为当前阶段阵容，起止时间等于 `rotation.period_begin/period_end` 且均在进行中；`Banners.json` 的限定池 2139 不再下发。`POST /admin/api/warps/rotation` 可切换当前阶段，未知阶段与未知模式返回 400。
- 设置页拒绝未知的 `Warps.RotationStartPhase`、格式错误的 `Warps.RotationStart`、未知 `Warps.Mode` 及越界 `Warps.RotationDays`，配置保持不变；已知阶段可以保存并立即生效。`warps` 分组共 8 项。
- `Warps.Story`、`Warps.StoryByChapter`、`Warps.PhaseLineup`（大小写不敏感，按枚举名保存）与 `Warps.CollabBanners` 可通过设置接口保存并在 `fields` 中读回；`/admin/api/warps` 随之返回 `story`、`story_by_chapter`、`story_active`、`phase_lineup`、`collab_banners`。未知 `Warps.PhaseLineup` 被拒绝且配置不变；随后恢复默认值（`Warps.StoryByChapter` 恢复为 false）。
- 切换 `Manual` 时以当前阵容初始化手动列表（滚动时间窗、默认光锥配对）。未知卡池、不合格卡池、重复卡池、未知物品、空 5★ 覆盖、开始时间不早于结束时间、错误配对及缺少 `entries` 均返回 400，原列表对象不变。
- 合法提交后 5★ 覆盖与显式时间窗反映在 `scheduled` 中，未设时间的条目跟随轮换周期，尚未开始的条目列出但不在进行中；空列表可以保存。
- `WarpSchedule.GetActiveBanners(realNow, playerNow)` 在玩家时间早 200 天时，把限定池时间窗平移到玩家时钟（窗口包含玩家时间、不包含真实时间），仍包含常驻池 1001，不含 2139。
- 读取仓库中的 `Config/Custom/Banners.history.json`：300 个卡池；补齐所有引用物品后全部合格，共 65 个阶段，`4.7-2` 的 `Overlap` 阵容（`WarpPhase.Overlapping`）包含 2142、3142、2146、2147、2148、3146、3147、3148；联动卡池 5001–5004 / 6001–6004 单独成组（`WarpPool.Collab`），不属于任何阶段；卡池库接口返回相同数量。
- 同时载入仓库中的 `Config/Custom/WarpItemNames.json`：卡池库接口的 `names` 中 `en` 的 1306 为 Sparkle、1501 为 Sparxie，且有 `zh-CN` 表。接口中 `4.7-2` 的 `banner_ids` 在 `Started` 下恰为 2146、2147、2148、3146、3147、3148；`Overlap` 下包含 2142 且不含任何联动卡池。
- 联动卡池：`CollabBanners` 开启时 `/admin/api/warps` 的 `collab_banners` 为 true，`collab` 恰为 5001–5004、6001–6004 且均在进行中，`scheduled` 不含它们；通过设置接口关闭后 `collab_banners` 为 false，`collab` 仍列出这 8 个卡池但都不在进行中。手动列表包含联动卡池 5001 时返回 400，原列表对象不变。检查后恢复原设置。

结束时恢复 `ServerOption.Warps` 默认值、原卡池配置并移除临时物品；`Config.json` 只写入临时目录。

## 账号克隆、存档快照与剧情进度

行为说明见 [剧情进度测试账号](../operations/story-checkpoints.md)。

- `SnapshotChecks.cs`：克隆得到新账号与请求的凭据，存档字段与昵称正确，不与源账号共享任何对象，不复制好友；修改克隆的货币与任务后，源账号在内存与直接读取的 SQLite 中都不变。克隆不存在的账号为 404，用户名冲突或源账号无存档时为 400 且不新增账号。快照按名称生成 id（重名追加 `-2`）、记录元数据并写入数据库目录下的 `Snapshots/`；列表、生成测试账号（自动生成密码可登录）、恢复到已有账号（内存与 SQLite 均替换，账号保留）均可用。`..%2F..`、`a.b` 等非法 id 及路径穿越返回 400，缺失快照 404；损坏的快照文件为 400 且不留下账号；未知表被忽略；无法反序列化的列为 400 且目标存档不变；删除后文件消失，重复删除 404。
- `StoryChecks.cs`（合成的两条主线，本回归不加载资源缓存）：未登录 401；剧情目录按版本与门槛顺序列出主线、章节、楼层和子任务，只列出任务存在的预设；未知主线、不属于该主线的子任务、未知停止位置/性别/预设均为 400 且不新增账号；缺少资源导致回放失败时不留下账号，对已有账号则恢复原存档；不存在的账号 404。真实剧情的成功回放（含 HTTP 接口）在登录与场景回归中验证。
- `EditorChecks.cs`（存档编辑，行为说明见 [存档编辑](../operations/player-editor.md)，本回归不加载资源缓存）：摘要报告离线、无撤销点；过期 `version` 的修改为 409 且不变；无资源时加载存档失败的修改返回 500、恢复原存档且不留下撤销点；原始数据列出存档表（不含 Account），Player 表可无损往返；修改 JSON 只替换该表（Uid 固定为目标账号，其余表保留，SQLite 同步），过期版本 409、格式错误 400、未知表 404；撤销恢复修改前的存档，再次撤销重做；租约被其他工具占用时修改与撤销均为 409；未知账号 404、无撤销点 404。下线删除：可通过接口标记；被新登录替换的会话、被工具租约停止的会话和普通账号的下线都不删除；标记账号下线后连同存档删除（内存与 SQLite）；克隆时可直接创建为下线删除账号（SQLite 中的列已写入）。需要加载存档的修改（资料、角色、物品、任务）在登录与场景回归中验证。

## 进度奖励设置

`ProgressionRewardSettingsChecks.cs` 通过 `PUT /admin/api/settings` 检查设置页 `rewards` 分组（行为说明见 [进度奖励](../operations/progression-rewards.md)）：分组共 16 项，默认关闭，主任务奖励为 `1:180`、`2:10000`、`101:1`，只勾选剧情战斗；未知物品、缺少数量和非正数量均被拒绝且原列表对象不变；开关、按行编辑的奖励列表（接受 `\r\n` 与空格分隔）和战斗类型可以一起保存。结束时恢复 `ServerOption.ProgressionRewards` 并移除临时物品。
