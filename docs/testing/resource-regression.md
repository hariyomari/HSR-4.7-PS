# 原始资源加载回归

项目：[`scripts/ResourceRegression/`](../../scripts/ResourceRegression/)。直接调用生产 `ResourceManager.LoadGameData()`，不启动服务、数据库或玩家对象，不读取生产 `Config.json`，不替换现有缓存。运营自定义配置与语言文件从仓库 `Config` 只读加载。

```powershell
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources目录> <新报告目录> --cache-out <新缓存文件>
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources目录> <新报告目录> --stale-cache <适配前缓存> --cache-out <新缓存文件>
```

`--cache-out` 可省略；指定时必须选择不存在的新文件。测试先做 MemoryPack 往返，再以 CreateNew 写入压缩快照。报告目录保存加载日志、audit.json 和隔离测试夹具。夹具故意触发缺文件与解析错误，最终生产资源加载阶段必须没有警告或错误，预期 `failures: 0, loader issues: 0`。

覆盖：可选表缺失允许加载、必需表缺失仍警告并禁止生成完整缓存、已存在的可选表解析错误仍失败、NPC 按角色声明的能力名解析共享配置、缺失或冲突的能力定义不能被当成空配置、新版 GridFight 枚举值、完整生产加载和快照往返。只读加载和缓存验证不能替代客户端实机任务或战斗验证。

OSBETAWin4.6.51 的四组 GridFight 枚举取自同 DLL 的显式枚举值。SpecificAvatarWithEquip 为 16，SpecificAvatarWithRandomEquip 为 17；旧资源缓存的数值含义可能不同，更新程序后应使用同版本资源生成新缓存，回滚时恢复配套程序和缓存。

该客户端索引不包含 DialogueDynamicContent、DialogueEvent、GridFightGenderOverride、ItemConfigTrainDynamic、RogueDLCBossBp、RogueWolfGunMiracleTarget、TrainPartyDynamicConfig。它们分别是没有当前读取方的历史/动态伴随表，或已有基础性别回退的覆盖表，按具体文件声明为可选；主 ItemConfig 等必需表不受影响。客户端实际包含的文件仍执行完整解析，不能用空 JSON 补齐不存在的表。

NPC 变体引用公共、原型怪物或 FakePlayer 配置，实体文件名不一定对应能力文件名。共享能力按角色 AbilityList 和 SkillList.EntryAbility 解析，冲突或真实缺失继续报错。未附带角色文件的 WhiteBox 开发记录不参与能力加载；这些测试记录不属于该客户端发布场景。

验证新缓存的 GridFight 回归可指定只读来源，程序会先复制到来源目录下的独立测试子目录：

```powershell
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj -- --cache <新缓存文件>
```

OSBETAWin4.6.51 新版表通过原始加载回归、缓存往返和 GridFight 路线回归。GridFightCampSeason 的 MonsterList 与基础阵营表合并，当前赛季通过阵营 ID 查询，其他赛季以内部复合键保留，发送的 ID 不改变。角色表没有重复 ID：第二赛季使用六位角色 ID，AvatarID 仍是基础角色 ID。新版不再要求 RoleSavedValueList，以原表的 GridFightRoleStar 解析技能；缓存记录该布局标志。第二赛季回血天赋使用 3011，阵型选择忽略 ID 为 0 的默认行。

`--stale-cache` 只在报告目录复制旧缓存，验证存在新版原表时能识别缺少适配结果的缓存，然后重新加载资源；不会替换生产缓存。旧版组合表保留原加载方式。按赛季筛选随机角色奖励，拒绝跨赛季角色加入。`IsFakeAvatar` 记录保留在缓存；帕姆 604202 没有普通 AvatarConfig，不加入普通角色池，NPC 专属战斗逻辑尚未实现。这些检查仍不能替代客户端实机战斗验证。

部署前备份程序与已有 `Config/Resource.bin`，确认资源路径后再切换到新程序和新缓存。加载器优先读取缓存；仅修改 ResourcePath 不会替换已有缓存。

## 4.7.0 正式服客户端只读探针

```powershell
dotnet run --project scripts/RetailResourceProbe -c Release -- <游戏安装目录> TurnBasedGameData <新的输出目录>
```

读取 `StreamingAssets` 与 `Persistent` 的 DesignV 索引，校验索引和所用数据块的内容摘要，同一键优先使用热更新条目。仅支持两个 GridFight 表和指定的 `MissionInfo` 记录；不是通用解包器。输出目录不得位于输入目录内，也不能已存在。

输出包括两个表的数值枚举 JSON、`3050321` 和 `4011900` 的 `MissionInfo`，以及带有块摘要、偏移、对照结果的 `report.json`。枚举名来自按行 ID 对照的参考数据，而非客户端中的字符串。两个相邻任务用作解码对照；任务主记录不包含单独的子任务或行为图。文件只供核对，不要直接覆盖服务器资源。退出码 `0` 表示探针通过，`2` 表示比较不一致，`1` 表示输入或控制校验失败。
