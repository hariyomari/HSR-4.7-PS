# 原始资源加载回归

[English](resource-regression.md) | 简体中文

`ResourceRegression` 在隔离进程中用生产加载器加载原始资源目录，并可导出经过验证的资源缓存。修改资源模型、按资源表读取的枚举或共享能力的解析方式后运行；从原始资源生成新的 `Resource.bin` 时也应运行。

项目：[`scripts/ResourceRegression/`](../../scripts/ResourceRegression/)

## 运行方式

从仓库根目录执行：

```powershell
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources目录> <新报告目录> --cache-out <新缓存文件>
dotnet run --project scripts/ResourceRegression/ResourceRegression.csproj -c Release -- <Resources目录> <新报告目录> --stale-cache <适配前缓存> --cache-out <新缓存文件>
```

- `--cache-out` 可省略；指定时必须是尚不存在的新文件。回归先做 MemoryPack 往返，全部检查通过后才以 CreateNew 写入压缩快照。
- `--stale-cache` 把旧缓存复制到报告目录，验证在新版原表存在时能识别缺少适配结果的缓存，然后重新加载资源。它不会替换生产缓存。

用新缓存运行 GridFight 回归时，把它作为只读来源传入，程序会先复制到自己的测试目录：

```powershell
dotnet run --project scripts/GridFightRegression/GridFightRegression.csproj -- --cache <新缓存文件>
```

## 前提条件

- 目标客户端版本的原始资源目录（`Resources`）。
- 回归直接调用生产 `ResourceManager.LoadGameData()`，不启动服务、数据库或玩家对象，不读取生产 `Config.json`，也不替换现有缓存。运营自定义配置与语言文件从仓库 `Config` 只读加载。
- 部署新缓存之前，备份程序与已有的 `Config/Resource.bin` 并确认资源路径，再同时切换到新程序和新缓存。加载器优先读取缓存，仅修改 ResourcePath 不会替换已有缓存。
- 只有客户端、没有完整原始资源时，按[从客户端导出重建资源缓存](../operations/resource-cache-from-client-export.zh-CN.md)处理。

## 覆盖范围

### 加载器行为

- 可选表缺失仍可加载；必需表缺失仍会告警并禁止生成完整缓存；已存在的可选表解析出错仍判定失败。
- 报告目录保存加载日志、`audit.json` 和隔离测试夹具。夹具故意触发缺文件和解析错误。
- ResMgr/ResCache 以调试级别记录单个资源文件缺失和加载诊断，这些只写入调试日志，因此回归从报告目录的 `loader-debug.log` 读回它们，并计入 `loader issues` 和 `audit.json` 的 `Warnings`。面向服主的告警与其他告警一样经由 `OnLogEntry` 送达。
- 最终的生产资源加载阶段不得产生任何警告或错误。
- 完整的生产加载和快照往返。

### 可选表

OSBETAWin4.7.51 客户端索引不包含 DialogueDynamicContent、DialogueEvent、GridFightGenderOverride、ItemConfigTrainDynamic、RogueDLCBossBp、RogueWolfGunMiracleTarget、TrainPartyDynamicConfig。它们是没有当前读取方的历史/动态伴随表，或已有基础性别回退的覆盖表，因此按具体文件声明为可选。主 ItemConfig 等必需表不受影响。客户端实际包含的文件仍完整解析；不存在的表不能用空 JSON 补齐。

### 共享能力

NPC 变体引用公共、原型怪物或 FakePlayer 配置，实体文件名不一定与能力文件名对应。共享能力按角色的 AbilityList 和 SkillList.EntryAbility 解析；冲突或真实缺失的定义继续报错，不会被当成空配置。未附带角色文件的 WhiteBox 开发记录不参与能力加载，这些测试记录不属于客户端的发布场景。

### GridFight 表

- 新版 GridFight 枚举值。
- GridFightCampSeason 的 MonsterList 与基础阵营表合并。当前赛季按阵营 ID 查询，其他赛季以内部复合键保留，发送给客户端的 ID 不变。
- 角色表没有重复 ID：第二赛季使用六位角色 ID，AvatarID 仍是基础角色 ID。
- 新版不再要求 RoleSavedValueList，按原表 GridFightRoleStar 解析技能，缓存记录该布局标志。
- 第二赛季的回血天赋为 3011；阵型选择忽略 ID 为 0 的默认行。
- 旧版组合表保留原加载方式。
- 随机角色奖励按赛季筛选，拒绝跨赛季角色。
- `IsFakeAvatar` 记录保留在缓存中。帕姆 604202 没有普通 AvatarConfig，不加入普通角色池。

## 预期结果

运行结束时输出 `Resource regression checks: <n>, failures: 0, loader issues: 0`，退出码为 0。

## 已知缺口

- **GridFight 枚举值与客户端不一致。** 当前代码中 `GridFightBonusTypeEnum`、`GridFightTraitEffectTypeEnum`、`GridFightConsumeTypeEnum` 的高位成员和 `GridFightEquipFuncTypeEnum.AddTraitLayer` 使用服务端占位值，现有的 `Config/Resource.bin` 也按占位值生成。因此 “beta bonus enum values match the client” 这一项失败，`--cache-out` 不会写出缓存。OSBETAWin4.7.51 客户端的取值为：
  - BonusType：SpecificAvatarWithEquip 16、SpecificAvatarWithRandomEquip 17、SpotLightReplaceCnt 18、Augment 19、SpotLightFreeRefreshCnt 20、TrailblazeShopCoin 21、ElationGoods 22、GlobalHp 23、ElationItem 24、AllAvatarByRarity 25。
  - TraitEffectType：BandRoleChoose 19、MoonGame 24、MoonGameSpotLight 25、DanHengTwoInOne 28、JadeShop 29、ClaraEnhance 31、AdditionalTraitRoleChoose 32、AhaUnique 33、GainOtherRoleTrait 34。
  - ConsumeType：UpgradeChimera 7、CopyChimera 8。
  - EquipFuncType：AddTraitLayer 12。

  OSBETAWin4.6.51 的四组 GridFight 枚举同样取自同一 DLL 的显式枚举值（SpecificAvatarWithEquip 16、SpecificAvatarWithRandomEquip 17）。这些枚举只按名称读表、不上协议，但改回客户端数值会改变缓存中整数的含义，必须与重新生成的缓存一起发布。更新程序后，应使用同版本资源生成新缓存；回滚时同时恢复配套的程序和缓存。
- 帕姆 604202 的 NPC 专属战斗逻辑尚未实现。
- 只读加载和缓存验证不能替代客户端实机的任务或战斗验证。

## 4.7.0 正式服客户端只读探针

```powershell
dotnet run --project scripts/RetailResourceProbe -c Release -- <游戏安装目录> TurnBasedGameData <新的输出目录>
```

探针读取 `StreamingAssets` 与 `Persistent` 的 DesignV 索引，校验索引和所用数据块的内容摘要，同一键优先使用热更新条目。仅支持两个 GridFight 表和指定的 `MissionInfo` 记录；不是通用解包器。输出目录不得位于输入目录内，也不能已存在。

输出包括两个表的数值枚举 JSON、`3050321` 和 `4011900` 的 `MissionInfo`，以及带有块摘要、偏移、对照结果的 `report.json`。枚举名来自按行 ID 对照的参考数据，而非客户端中的字符串。两个相邻任务用作解码对照；任务主记录不包含单独的子任务或行为图。文件只供核对，不要直接覆盖服务器资源。退出码 `0` 表示探针通过，`2` 表示比较不一致，`1` 表示输入或控制校验失败。
