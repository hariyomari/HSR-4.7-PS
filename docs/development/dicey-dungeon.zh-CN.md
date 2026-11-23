# 骰子收容（DiceyDungeon）

[English](dicey-dungeon.md) | 简体中文

> **本分支不编译此功能。** 由于其命令与消息在当前使用的 4.7 协议中缺失或不一致，DiceyDungeon 已从构建中排除（[`GameServer/GameServer.csproj`](../../GameServer/GameServer.csproj) 中的 `<Compile Remove>`）。其 `Config/Custom/DiceyDungeon.json` 仍随服务器发布并会被加载。

本文说明 4.8 新增的骰子收容活动在服务端的实现：未命名的协议、配置来源、服务端执行的规则，以及客户端如何找到活动的入口 NPC。修改该活动、重新导出其配置或排查入口按钮无反应的问题之前请先阅读。

## 概览

活动「Containment Starts With a Roll」于 4.8（OSBETAWin4.7.51 客户端）新增。客户端模块 `RPG.Client.DiceyDungeon`，活动 `50140`，面板 `50140`，活动模块 `5014001`。骰子战斗完全在客户端运行，服务端负责一局的进度：在打哪一关、哪个节点，角色、血量、已装备技能与奇物、已通过节点，以及每次胜利后的奖励选择。

| 内容 | 路径 |
| --- | --- |
| 玩法逻辑（玩家组件） | `GameServer/Game/Activity/DiceyDungeon/DiceyDungeonManager.cs` |
| 请求处理器 | `GameServer/Server/Packet/Recv/DiceyDungeon/HandlerDiceyDungeon.cs` |
| 响应包 | `GameServer/Server/Packet/Send/DiceyDungeon/PacketDiceyDungeon.cs` |
| 存档表 `PlayerDiceyDungeon` | `Common/Database/DiceyDungeon/PlayerDiceyDungeonData.cs` |
| 配置 | `Config/Custom/DiceyDungeon.json`，模型 `Common/Data/Custom/DiceyDungeonConfig.cs` |
| 活动任务 | `GameServer/Game/Mission/FinishType/Handler/MissionHandlerDiceyDungeonFinish.cs` |
| 排期 | `Config/Custom/ActivityConfig.json` 中 `5014001` / 面板 `50140` |
| 回归 | `scripts/LoginSceneRegression` 的 DiceyDungeon 段 |

## 协议

4.8 dump 新增的命令目录 `LDJDDDDPLLH`（10643–10659）整组未命名，消息与字段均保持混淆名，没有写进 4.8 master。含义来自客户端代码：发请求的是网络服务类 `LCBFIHEMOOC`，其异步状态机保留了真实方法名（`GetActivityDataAsync`、`EnterNodeAsync`、`FinishNodeAsync`、`_SendSelectRewardAsync`、`RerollRewardAsync`）；响应由数据模型 `FLIFOAOEFPP` / `MJHOIBEANAJ` 读取，后者的自动属性保留了 `<LevelID>k__BackingField` 一类真实名。字段对应关系通过反汇编请求构造与响应同步代码、比对存储偏移得到。混淆器按原始标识符生成名字，同名混淆名在不同类中含义一致（例如 `JKCOAIGJMMM` 在所有响应中都是 retcode）。

| cmdId | 消息 | 方向 | 字段 |
| --- | --- | --- | --- |
| 10646 | `DLDDOMMLPBK` | 请求：获取数据 | 无 |
| 10659 | `OBJJEHIKFJC` | 响应 | `FJCAKOIDONG` 数据（`LCIKABPABHK`），`JKCOAIGJMMM` retcode |
| 10658 | `NOJMGKOMJHP` | 请求：进入节点 | `IJAPHCOHMIN` 关卡，`CDMFHNGEFCN` 节点，`ILHPFNCIOJN` 角色，`JHLGHLBDMLH` 技能，`KIIIDKADAIN` 奇物 |
| 10652 | `OHCCEANPCIL` | 响应 | 关卡、节点、`NBNKLEBNPFA` 一局数据、retcode |
| 10654 | `PIEGCFMAEOO` | 请求：结算节点 | 关卡、节点、`EKFDEEHCKDH` 是否胜利、`GNGBGCHGJPD` 剩余血量、`BOILBFJANKL` 敌方剩余血量、`BJKGNCLCKBG` 回合数、`LALKFADHJJL` 耗时 |
| 10655 | `NMNFJDBNBPG` | 响应 | 关卡、节点、是否胜利、`NIMINCMIMEE` 本关已结算、`BDCOFIOCJFO` 星数、`CCBNBBGBKCE` 最远节点（0 表示不更新）、一局数据、retcode |
| 10657 | `KPNDLNCAECC` | 请求：选择奖励 | oneof：`ALPKKNKAFBG`(9) 技能 `{GHMHMKINNEJ 技能, KFGFGMFAMDA 被替换的技能}`，`ANBKDJKHCBI`(4) 奇物 `{LJCEOGPOGBE}`，`POJDOCAKCJM`(11) 放弃，`NBNMHLIFCMF`(8) 未见客户端使用，按放弃处理 |
| 10656 | `GOPAMMODIJI` | 响应 | retcode、一局数据 |
| 10645 | `IMPHFFIJDKP` | 请求：重随奖励 | oneof：`ALPKKNKAFBG`(13) 技能，`ANBKDJKHCBI`(2) 奇物 |
| 10651 | `HJLMNPJFEMI` | 响应 | oneof：`DPLODKBIJDI`(6) 技能选择，`CACADJOIHHI`(14) 奇物选择；retcode |
| 10648 | `MDOHKDNNDPI` | 请求 | 无字段。客户端 C# 中没有构造它的代码，应来自 Lua，用途不明 |
| 10643 | `LHDOHPHPABA` | 响应 | 仅 retcode；服务端只回成功，不改动进度 |

一局数据 `HBBPNKMJFBF`：`IJAPHCOHMIN` 关卡，`ILHPFNCIOJN` 角色，`JCLIFKACIJD` 战斗序号（SerialNumber），`FHHOPLLDPAP` 当前节点，`OMIIAOCIIPA` 当前血量，`JHLGHLBDMLH` 技能，`KIIIDKADAIN` 奇物，`BAFLDOEHBJK` 已通过节点，`DPLODKBIJDI` 待选技能，`CACADJOIHHI` 待选奇物。待选奖励 `MHLHDALAFMJ`：`GGBGEJIIKFN` 候选，`MNBMPEIABNJ` 放弃时可回血，`LBDOBLGKANH` 剩余重随次数。关卡记录 `OGDOODOGIOF`：`IJAPHCOHMIN` 关卡，`HCOFMFKFMDK` 已完成，`BDCOFIOCJFO` 星数，`GKALFLHFPKC` 最远节点。

客户端状态完全由响应推导：有待选奖励（候选列表非空，技能优先于奇物）进入 PendingReward，否则 PendingChallengeNode；结算响应 `NIMINCMIMEE` 为 true 时进入 LevelSettling 并忽略一局数据。

## 配置来源

关卡、节点、角色、怪物、技能池、奇物池与常量都在客户端 ExcelOutput 表里，但这些表的路径不在社区名单中，资源导出不会产出，表类与行类型名也被混淆。`Config/Custom/DiceyDungeon.json` 由 Demiurge 按行类型对 DesignData 中的未命名资源逐一试解、再用关卡与节点 ID 交叉确认得到，文件内 `_source` 记录了各表的资源哈希：

| 内容 | 行类型 | 资源哈希 |
| --- | --- | --- |
| 关卡（5 行，1–4 剧情、5 挑战） | `JCDIPAECJHB` | `6ab08a7d3f739d62` |
| 节点（29 行，按 `PreNodeID` 成链） | `ICPBAAIKFFE` | `c1643faeeb64c1d6` |
| 怪物血量 | `BGCDHJCDNIA` | `2f85659fbe0aa4ca` |
| 角色（初始血量 1000、剧情 / 挑战技能） | `FCCIAHCGLDN` | `df826799821d351a` |
| 技能池 / 奇物池 | `CJPDKEFCJNI` / `OBBGJBLAJKE` | `427780f4afd6d5c4` / `215e3f5962540f7e` |
| 常量（最多 5 个技能、挑战最多 3 个奇物、重随 1 次、放弃回血 100、星级 80/60/20） | `GHPMCNKGPMF` | `4288f0425c3c43d9` |

该文件不进入 `Resource.bin`，每次启动从 `Config/Custom` 读取。客户端更新后需要按同样方法重新导出。

## 规则

- 进入某关第一个节点即开新局，其他关卡的进行中局被丢弃。剧情关使用关卡指定角色与其剧情初始技能；挑战关只接受关卡列出的角色，技能与奇物须来自该角色的挑战技能 / 奇物且不超过上限。前置关卡未完成时拒绝进入。
- 只能进入「当前节点（未通过）」或「已通过的当前节点的下一个」；有待选奖励时不能前进。每次进入领取新的战斗序号。
- 剧情关失败：保持在原节点，可重试。胜利：记录节点与剩余血量（不超过初始血量）。最后一个节点胜利即本关完成并清空本局；否则给出奖励选择：每次胜利一个技能选择，精英节点再加一个奇物选择。节点配置了固定候选时用固定候选，否则按权重从角色的池子里抽 3 个未拥有的。
- 通过节点 103 之后，奖励才允许重随（1 次）与放弃回血（+100，不超过初始血量）。
- 挑战关只有一个节点，每次结算都会结束本局：胜利 3 星，否则按造成伤害占 Boss 血量的比例（≥80% 3 星、≥60% 2 星、≥20% 1 星）。保留最好成绩，1 星以上视为完成。
- 活动任务：`DiceyDungeonNodeFinish`（480600）/ `DiceyDungeonLevelFinish`（480601）按 `ParamIntList` 中的节点 / 关卡 ID 计数。

## 入口

活动没有独立的入口界面：面板 50140 的「前往参与」只是一次跳转，玩法从场景里的入口 NPC 打开。相关配置都在内容包 `150137`（`ContentPackageConfig`）上：引导任务 `8048208`、隐藏的常驻任务 `8048209`，引导入口 `2058104`，常驻入口 `2058105`（场景 `20581001` 第 159 组、锚点 1）。

客户端的判断链（`CommonActivityGotoBtnBehaviour.on_btn_go`，状态来自 `ActivityStateData._RefreshState_Internal`）：

1. `ActivityPanelCondition` 的 `GuideConditions`（`FinishMainMission:8048208`）不满足时走引导跳转 `6428`（`MultiMainMission`）；满足后走活动跳转 `6427`（`AreaSwitch [2058105, 2589, 150137]`）。
2. 活动跳转在目标场景的地图数据里查找地图图标 `2589`（`MappingInfo` 类型 `ACTIVITY_ENTRANCE`），图标挂在第 159 组的 NPC `3315` 上；找到后打开地图并定位到它。
3. 第 159 组是客户端组，`OwnerMainMissionID` 为 `8048209`。官方在活动期间让这个任务一直处于进行中，组才存在。
4. 进入场景后，客户端遍历未完成的子任务，激活每个子任务配置里 `GroupIDList` 列出的组（`MapDef.InitActivateHoyoGroupByMission`，要求子任务的 `LevelFloorID` 是当前场景）。`8048209` 有两步（客户端 `MissionInfo_8048209.partial.json`，该资源不在导出名单里，按路径哈希解出）：`804820901`（`Auto`，`SubMissionFinishCnt [804820801]`，等引导的第一步完成，`GroupIDList` 为空）和 `804820902`（`AnySequence [804820901]`，`Talk`，`GroupIDList [159]`）。所以必须是 `804820902` 处于进行中，入口 NPC 才会生成；只把主线报成未完成，或者把 `804820901` 报成进行中，都不够。更早的活动是同样的结构（内容包 `150077` 的 `8043204`：`804320401` / `804320402`，后者挂着入口组 182）。

服务端为此做了三件事。前两件缺任何一件，按钮都会「点了没反应」，而且客户端不报错；缺第三件则是传送到位但现场没有 NPC：

- **引导任务的完成状态前后一致。** 客户端会把场景任务信息（`MissionStatusBySceneInfo`）里的已完成 / 未完成主线列表写进 `MainMissionData.IsFinish`，`FinishMainMission` 条件读的就是这个标记。`BuildSceneMissionInfo` 为了让活动入口保持可见，会把内容包的任务补进「未完成」列表；如果引导任务也被这样补进去，玩家进过一次该场景后按钮就退回引导跳转，而引导任务没有可执行的内容。因此，凡是本次会话里 `GetMissionStatusScRsp` 已经报告为完成的主线，场景任务信息里继续报告为完成（`MissionManager.NoteMainMissionsReportedFinished`）；客户端没有问过的任务（常驻任务 `8048209`）仍然保持未完成。
- **常驻入口组进入地图数据。** `MissionManager.IsGroupOwnerMissionActive` 取代了原来「所属主线必须是进行中」的判断：内容包常驻入口的起始组（`ContentPackageConfigExcel.TryGetStandingEntrance`：有活动模块、引导入口与常驻入口不同、常驻入口的起始组归内容包自己的任务所有）在所属任务读作「已完成」时也算有效。关闭任务系统时所有任务都读作已完成；开启时，缺子任务数据而被关闭的常驻任务同样如此。任务尚未到达（`None`）或活动不在排期内时，该组仍然不出现。
- **常驻任务停在最后一步。** 对上面这种「开放中的常驻入口」，场景任务信息把常驻任务放进未完成列表，把它的 `FinishSubMissionList`（最后一步，`804820902`）以 `MissionDoing` 下发，`StartSubMissionList`（`804820901`）以 `MissionFinish` 下发（`MissionManager.AddStandingEntranceMissions`）；`GetMissionStatusScRsp` 对同一批主线 / 子任务给出同样的答案，不会把常驻任务报成完成。服务端资源里没有这两个子任务的定义，只有主线的起始 / 结束子任务列表，所以按「结束列表是停留的那一步」处理；任务系统开启且子任务在资源里有定义时，使用它的真实状态。

`8048208` / `8048209` 的子任务数据不在当前资源里（启动时 `ReportTruncatedMissionGraphs` 会点名），开启任务系统时引导任务无法正常推进。

## 限制

- 入口部分是从客户端 Lua 字节码与 C# 反汇编读出来的，服务端回归只验证了下发的数据（地图组列表、场景任务信息），按钮与入口 NPC 需要实机确认。
- 推断而非抓包确认的部分：固定候选（节点的 Fixed* 列表）作为该节点之后的奖励、精英节点额外给奇物、剧情关星数保持 0、挑战星级按伤害比例计算、`NBNMHLIFCMF` 按放弃处理、`MDOHKDNNDPI` 的用途。
- `MissionFinishTypeEnum` 新增的两个值只有在用当前代码重新生成 `Resource.bin` 后才会出现在缓存的 `FinishWay` 行上；旧缓存里这些任务的 FinishType 是 `Unknown`，活动任务不会推进。
- 未经客户端实机验证。
